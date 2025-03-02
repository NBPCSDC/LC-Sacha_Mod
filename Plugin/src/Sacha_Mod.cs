using System.Collections;
using System.Diagnostics;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace Sacha_Mod
{

    // You may be wondering, how does the Example Enemy know it is from class SachaAI?
    // Well, we give it a reference to to this class in the Unity project where we make the asset bundle.
    // Asset bundles cannot contain scripts, so our script lives here. It is important to get the
    // reference right, or else it will not find this file. See the guide for more information.

    class SachaAI : EnemyAI
    {
        // We set these in our Asset Bundle, so we can disable warning CS0649:
        // Field 'field' is never assigned to, and will always have its default value 'value'
#pragma warning disable 0649
        public Transform turnCompass = null!;
        public Transform attackArea = null!;
        public Transform view = null!;
        public IKControl ik_Control = null!;
        public AudioClip[] hi_SFX = null!;
        public AudioClip[] clap_SFX = null!;
        public AudioClip[] combat_SFX = null!;
        public AudioClip[] attack_SFX = null!;
        public AudioClip[] hit_SFX = null!;
        public AudioClip[] randomSound_SFX = null!;
        public AudioClip flee_SFX = null!;
        public AudioClip song_SFX = null!;
        public AudioClip stopTarget_SFX = null!;
        private AudioClip currentAudioClip = null!;

#pragma warning restore 0649
        Transform farestNodeFromPos = null!;
        float timeSinceHittingLocalPlayer;
        float attackDelay;
        float attackedDelay;
        float tryAttackDelay; // Delay betwenn attack start and Sacha touch the player
        float randomSoundDelay;
        float emoteDelay;
        bool isFirstEncounter;
        bool isSongPlaying = false;
        bool helloDone = false;
        Vector3 targetPosition;
        System.Random enemyRandom = null!;
        bool isDeadAnimationDone;
        float walkSpeed = 2f; // Vitesse de marche
        float runSpeed = 4f;  // Vitesse de course
        bool isWalking = false;
        bool isRunning = false;
        bool hasClaped = false;
        float stamina = 10f;
        float animationMaxDelay = 10f;
        float maxTargetDistance = 15f;
        bool isStaminaFilling;
        float backupDistance = 3f;
        float idleDistance = 4f;
        bool updateRotation = true;
        float runDistance = 6f;  // Seuil de distance à partir duquel l'ennemi court

        bool useVoiceSFX = true;

        enum State
        {
            SearchingForPlayer,
            FollowPlayer,
            IsInCombat,
            RunAway,
            AttackInProgress,
            ReceiveHitInProgress,
            EmotePlaying,
            HelloInProgress,
            ClapInProgress
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        public override void Start()
        {
            base.Start();
            LogIfDebugBuild("Sacha Spawned");
            creatureAnimator.applyRootMotion = false;
            timeSinceHittingLocalPlayer = 0;
            isFirstEncounter = true;
            attackDelay = 0;
            attackedDelay = 0;
            tryAttackDelay = 0;
            enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
            isDeadAnimationDone = false;
            // NOTE: Add your behavior states in your enemy script in Unity, where you can configure fun stuff
            // like a voice clip or an sfx clip to play when changing to that specific behavior state.
            currentBehaviourStateIndex = (int)State.SearchingForPlayer;
            // We make the enemy start searching. This will make it start wandering around.
            StartSearch(transform.position);
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead)
            {
                // For some weird reason I can't get an RPC to get called from HitEnemy() (works from other methods), so we do this workaround. We just want the enemy to stop playing the song.
                if (!isDeadAnimationDone)
                {
                    LogIfDebugBuild("Stopping enemy voice with janky code.");
                    isDeadAnimationDone = true;
                    creatureVoice.Stop();
                    creatureVoice.PlayOneShot(dieSFX);
                    StopSongClientRpc();
                }
                return;
            }

            timeSinceHittingLocalPlayer += Time.deltaTime;
            attackDelay += Time.deltaTime;
            randomSoundDelay += Time.deltaTime;

            var state = currentBehaviourStateIndex;

            switch (state)
            {
                case (int)State.AttackInProgress:
                    tryAttackDelay += Time.deltaTime;
                    break;
                case (int)State.EmotePlaying:
                    emoteDelay += Time.deltaTime;
                    break;
                case (int)State.IsInCombat:
                    attackedDelay += Time.deltaTime;
                    break;
                default:
                    break;
            }


            if (targetPlayer != null && state != (int)State.AttackInProgress)
            {
                if (updateRotation)
                {
                    turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
                    transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 4f * Time.deltaTime);
                }
                view.transform.position = targetPlayer.gameplayCamera.transform.position; // Only for head
            }
            if (stunNormalizedTimer > 0f)
            {
                agent.speed = 0f;
            }

            Vector3 direction = targetPosition - transform.position;
            direction = direction.normalized;
            float dotProduct = Vector3.Dot(transform.forward, direction);

            UpdateAnimationClientRpc(agent.velocity.magnitude, dotProduct < 0);

            isRunning = agent.velocity.magnitude > walkSpeed + 0.1f;
            isWalking = agent.velocity.magnitude > 0.1f && !isRunning;

        }


        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            {
                return;
            };
            switch (currentBehaviourStateIndex)
            {
                case (int)State.SearchingForPlayer:
                    handleSearchingForPlayer();
                    break;
                case (int)State.FollowPlayer:
                    handleFollowPlayer();
                    break;
                case (int)State.IsInCombat:
                    handleCombatState();
                    break;
                case (int)State.RunAway:
                    break;
                case (int)State.AttackInProgress:
                    break;
                case (int)State.ReceiveHitInProgress:
                    break;
                case (int)State.HelloInProgress:
                    break;
                case (int)State.EmotePlaying:
                    handleEmote();
                    break;
                case (int)State.ClapInProgress:
                    break;
                default:
                    LogIfDebugBuild("This Behavior State doesn't exist!");
                    break;
            }

            updateStamina();
        }

        private void handleSearchingForPlayer()
        {
            if (!isFirstEncounter)
            {
                if (tryDoRandomAnimation())
                {
                    StopSearch(currentSearch);
                    return;
                }
            }

            agent.speed = walkSpeed;
            ik_Control.ikActive = false;
            if (FoundClosestPlayerInRange(maxTargetDistance, 3f))
            {
                LogIfDebugBuild("Start Target Player");
                ik_Control.ikActive = true;
                if (isFirstEncounter)
                {
                    isFirstEncounter = false;
                    SwitchToBehaviourClientRpc((int)State.HelloInProgress);
                    StartCoroutine(PlayHelloAndWait());
                    playSong();
                }
                else
                {
                    if (helloDone)
                    {
                        if (enemyRandom.Next(0, 3) == 0)
                        {
                            playSong();
                        }
                        SwitchToBehaviourClientRpc((int)State.FollowPlayer);
                    }
                }
                targetPosition = transform.position;
                SetDestinationToPosition(targetPosition, checkForPath: false);
                StopSearch(currentSearch);
                return;
            }
            TryPlayRandomSound();
        }

        private void handleCombatState()
        {
            if (!isPlayerStillTarget())
            {
                return;
            }
            if (attackedDelay > 10f)
            {
                attackedDelay = 0;
                creatureAnimator.SetBool("isCombat", false);
                SwitchToBehaviourClientRpc((int)State.FollowPlayer);
            }
            else
            {
                creatureAnimator.SetBool("isMarteloAttack", false);
                if (!tryAttack(20))
                {
                    FollowPlayer();
                }
            }
        }

        private void handleFollowPlayer()
        {
            if (!isPlayerStillTarget())
            {
                return;
            }
            TryPlayRandomSound();

            if (!reactToEmote())
            {
                creatureAnimator.SetBool("isMarteloAttack", true);
                if (!tryAttack(40))
                {
                    if (!tryDoRandomAnimation())
                    {
                        FollowPlayer();
                    }
                }
            }
        }

        private void handleRunAway()
        {
            if (farestNodeFromPos == null)
            {
                SwitchToBehaviourClientRpc((int)State.FollowPlayer);
                DoAnimationClientRpc("stopRunAway");
            }
            else if (!TargetClosestPlayerInAnyCase() || Vector3.Distance(transform.position, farestNodeFromPos.position) <= 10f)
            {
                StartSearch(transform.position);
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                DoAnimationClientRpc("stopRunAway");
                updateRotation = true;
                ik_Control.ikActive = true;
            }
        }

        private void resetEmote()
        {
            emoteDelay = 0;
            DoAnimationClientRpc("stopEmote");
            switchBackToRoutineState();
        }


        private void TryPlayRandomSound()
        {
            if (randomSoundDelay > 15f && !isVoiceSoundPlaying())
            {
                randomSoundDelay = 0;
                playRandomSoundFromSounds(randomSound_SFX);
            }
        }


        bool isVoiceSoundPlaying()
        {
            return creatureVoice.isPlaying;
        }

        bool isPlayerStillTarget()
        {
            if (targetPlayer == null) return false;

            float distanceToPlayer = Vector3.Distance(transform.position, targetPlayer.transform.position);
            if (distanceToPlayer > maxTargetDistance && (!TargetClosestPlayerInAnyCase() || !CheckLineOfSightForPosition(targetPlayer.gameplayCamera.transform.position)))
            {
                LogIfDebugBuild("Stop Target Player");
                StartSearch(transform.position);
                creatureAnimator.SetBool("isCombat", false);
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                playSound(stopTarget_SFX, true);
                StopSongClientRpc();
                return false;
            }
            return true;
        }


        void handleEmote()
        {
            if (targetPlayer != null)
            {
                if (emoteDelay >= animationMaxDelay || Vector3.Distance(transform.position, targetPlayer.transform.position) > runDistance)
                {
                    resetEmote();
                }
            }
            else
            {
                if (emoteDelay >= animationMaxDelay)
                {
                    resetEmote();
                }
            }
        }



        bool tryAttack(int chance)
        {
            if (attackDelay > 2f)
            {
                if (enemyRandom.Next(0, chance) == 0)
                {
                    // Attack
                    attackDelay = 0;
                    SwitchToBehaviourClientRpc((int)State.AttackInProgress);
                    StartCoroutine(attack());
                    return true;
                }
            }
            return false;
        }

        bool reactToEmote()
        {
            if ((targetPlayer.performingEmote || targetPlayer.disableLookInput)) {
                if (!hasClaped) {
                    SwitchToBehaviourClientRpc((int)State.ClapInProgress);
                    StartCoroutine(clap());
                    hasClaped = true;
                }
            }
            else
            {
                hasClaped = false;
            }
            return hasClaped;
        }

        void updateStamina()
        {
            if (isRunning)
            {
                stamina -= 0.2f;
                if (stamina < 0)
                {
                    isStaminaFilling = true;
                }
            }
            else
            {
                stamina += 0.2f;
                if (stamina >= 10f)
                {
                    isStaminaFilling = false;
                }
            }
            stamina = Mathf.Clamp(stamina, 0, 10);
        }

        bool hasStamina()
        {
            return stamina > 0 && !isStaminaFilling;
        }

        bool FoundClosestPlayerInRange(float range, float senseRange)
        {
            TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: true);
            if (targetPlayer == null)
            {
                // Couldn't see a player, so we check if a player is in sensing distance instead
                TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: false);
                range = senseRange;
            }
            return targetPlayer != null && Vector3.Distance(transform.position, targetPlayer.transform.position) < range;
        }

        bool TargetClosestPlayerInAnyCase()
        {
            mostOptimalDistance = 2000f;
            targetPlayer = null;
            for (int i = 0; i < StartOfRound.Instance.connectedPlayersAmount + 1; i++)
            {
                tempDist = Vector3.Distance(transform.position, StartOfRound.Instance.allPlayerScripts[i].transform.position);
                if (tempDist < mostOptimalDistance)
                {
                    mostOptimalDistance = tempDist;
                    targetPlayer = StartOfRound.Instance.allPlayerScripts[i];
                }
            }
            if (targetPlayer == null) return false;
            return true;
        }

        void FollowPlayer()
        {
            if (targetPlayer == null || !IsOwner) return;

            // Calcul de la distance entre l'ennemi et le joueur
            float distanceToPlayer = Vector3.Distance(transform.position, targetPlayer.transform.position);

            if (distanceToPlayer > runDistance)
            {
                // Si trop éloigné, l'ennemi court vers le joueur
                if (hasStamina())
                {
                    agent.speed = runSpeed;
                }
                else
                {
                    agent.speed = walkSpeed;
                }
                targetPosition = targetPlayer.transform.position;
                SetDestinationToPosition(targetPosition); // Avance vers le joueur
            }
            else if (distanceToPlayer > idleDistance)
            {
                // Si dans la plage de marche, l'ennemi marche vers le joueur
                agent.speed = walkSpeed;
                targetPosition = targetPlayer.transform.position;
                SetDestinationToPosition(targetPosition); // Avance lentement
            }
            else if (distanceToPlayer < backupDistance)
            {
                // Si trop proche, l'ennemi recule en fonction du vecteur direction
                agent.speed = walkSpeed;

                // Calcul du vecteur entre l'ennemi et le joueur
                Vector3 directionToPlayer = targetPlayer.transform.position - transform.position;
                targetPosition = -directionToPlayer.normalized; // Vecteur opposé, normalisé
                targetPosition = transform.position + targetPosition * 2f;

                // Déplacer l'ennemi vers cette position
                SetDestinationToPosition(targetPosition);

            }
            else
            {
                // Si dans la zone d'intervalle, l'ennemi reste immobile
                agent.speed = 0;
            }
        }

        IEnumerator clap()
        {
            agent.speed = 0;
            yield return new WaitForSeconds(0.5f);
            playRandomSoundFromSounds(clap_SFX);

            DoAnimationClientRpc("clap");

            yield return new WaitForSeconds(3f);

            switchBackToRoutineState();
        }


        // Lancer l'animation et attendre qu'elle se termine
        IEnumerator PlayHelloAndWait()
        {
            yield return new WaitUntil(() => IsPlayerLookingAtEnemy());

            yield return new WaitForSeconds(0.5f);

            playRandomSoundFromSounds(hi_SFX);
            DoAnimationClientRpc("hello");

            yield return new WaitForSeconds(2f);

            helloDone = true;
            SwitchToBehaviourClientRpc((int)State.FollowPlayer);
        }

        IEnumerator attack()
        {
            if (targetPlayer == null || Vector3.Distance(transform.position, targetPlayer.transform.position) >= idleDistance)
            {
                switchBackToRoutineState();
                yield break;
            }
            agent.speed = 4f;
            tryAttackDelay = 0f;
            float distanceToPlayer = Vector3.Distance(transform.position, targetPlayer.transform.position);
            while (distanceToPlayer > 1f)
            {
                if (distanceToPlayer > runDistance || tryAttackDelay >= 6f)
                {
                    switchBackToRoutineState();
                    yield break;
                }

                if(targetPlayer != null)
                {
                    Vector3 direction = (transform.position - targetPlayer.transform.position).normalized;
                    targetPosition = targetPlayer.transform.position + direction * 0.5f;
                    SetDestinationToPosition(targetPosition);
                    distanceToPlayer = Vector3.Distance(transform.position, targetPlayer.transform.position);
                    yield return null;
                }
                else
                {
                    switchBackToRoutineState();
                    yield break;
                }

            }
            DoAnimationClientRpc("attack");
            yield return new WaitUntil(() =>
                !(creatureAnimator.GetCurrentAnimatorStateInfo(0).IsName("Hit") ||
                creatureAnimator.GetCurrentAnimatorStateInfo(0).IsName("martelo")) ||
                creatureAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1.0f
            );
            attackHitClientRpc();
            switchBackToRoutineState();
        }

        IEnumerator TakeHit(int force)
        {
            SwitchToBehaviourClientRpc((int)State.ReceiveHitInProgress);
            ik_Control.ikActive = false;
            creatureAnimator.SetInteger("hitIndex", enemyRandom.Next(0, 2));
            DoAnimationClientRpc("hit");
            playRandomSoundFromSounds(hit_SFX);

            if (enemyHP - force <= 0)
            {
                StopCoroutine(attack());
                StopSongClientRpc();
                SwitchToBehaviourClientRpc((int)State.RunAway);
                creatureAnimator.SetBool("isCombat", false);
                playSound(flee_SFX, true);
                DoAnimationClientRpc("runAway");
                updateRotation = false;
                farestNodeFromPos = ChooseFarthestNodeFromPosition(targetPlayer.transform.position);
                targetPosition = farestNodeFromPos.position;
                SetDestinationToPosition(targetPosition, checkForPath: true);
                agent.speed = runSpeed * 3f;
            }
            else
            {
                SwitchToBehaviourClientRpc((int)State.IsInCombat);
                creatureAnimator.SetBool("isCombat", true);
                attackedDelay = 0f;
                ik_Control.ikActive = true;
                yield return new WaitForSeconds(0.5f);
                playRandomSoundFromSounds(combat_SFX);
            }
            yield break;
        }


        /**
         * Routine States are all states we need for Sacha to work such as Search, Follow or Combat 
        */
        void switchBackToRoutineState()
        {
            agent.speed = walkSpeed;
            if(targetPlayer != null)
            {
                if (attackedDelay != 0)
                {
                    SwitchToBehaviourClientRpc((int)State.IsInCombat);
                }
                else
                {
                    SwitchToBehaviourClientRpc((int)State.FollowPlayer);
                }
            }
            else
            {
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            }

        }


        bool IsPlayerLookingAtEnemy()
        {
            if (targetPlayer == null) return false;

            Vector3 directionToEnemy = (transform.position - targetPlayer.gameplayCamera.transform.position).normalized;
            float dotProduct = Vector3.Dot(targetPlayer.gameplayCamera.transform.forward, directionToEnemy);

            if (dotProduct > 0.50f)
            {
                return CheckLineOfSightForPosition(targetPlayer.gameplayCamera.transform.position);
            }
            return false;
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            if (timeSinceHittingLocalPlayer < 1f || currentBehaviourStateIndex == (int)State.RunAway || currentBehaviourStateIndex == (int)State.AttackInProgress) return;

            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
            if (playerControllerB != null)
            {
                if(currentBehaviourStateIndex == (int)State.EmotePlaying)
                {
                    resetEmote();
                }
                LogIfDebugBuild("Sacha Collision with Player!");
                timeSinceHittingLocalPlayer = 0f;
                creatureAnimator.SetBool("isMarteloAttack", false);
                DoAnimationClientRpc("attack");
                playRandomSoundFromSounds(attack_SFX);
                playerControllerB.DamagePlayer(25);
                switchBackToRoutineState();
            }
        }

        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if (isEnemyDead) return;

            enemyHP -= force;
            if (IsOwner)
            {
                if (enemyHP <= 0 && !isEnemyDead)
                {
                    // Our death sound will be played through creatureVoice when KillEnemy() is called.
                    // KillEnemy() will also attempt to call creatureAnimator.SetTrigger("KillEnemy"),
                    // so we don't need to call a death animation ourselves.

                    StopCoroutine(attack());
                    StopCoroutine(TakeHit(force));
                    // We need to stop our search coroutine, because the game does not do that by default.
                    StopCoroutine(searchCoroutine);
                    ik_Control.ikActive = false;
                    KillEnemyOnOwnerClient();
                }
                else
                {
                    StartCoroutine(TakeHit(force));
                }
            }
        }

        bool tryDoRandomAnimation()
        {
            if (enemyRandom.Next(0, 60) == 0)
            {
                creatureAnimator.SetInteger("randomEmote", enemyRandom.Next(0, 4));
                DoAnimationClientRpc("startEmote");
                SwitchToBehaviourClientRpc((int)State.EmotePlaying);
                agent.speed = 0;
                return true;
            }
            return false;
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName)
        {
            LogIfDebugBuild($"Animation: {animationName}");
            creatureAnimator.SetTrigger(animationName);
        }

        [ClientRpc]
        public void UpdateAnimationClientRpc(float speed, bool isGoingBackward)
        {
            creatureAnimator.SetFloat("speed", speed);
            creatureAnimator.SetBool("goBackward", isGoingBackward);
        }

        [ClientRpc]
        public void attackHitClientRpc()
        {
            playRandomSoundFromSounds(attack_SFX);
            int playerLayer = 1 << 3; // This can be found from the game's Asset Ripper output in Unity
            Collider[] hitColliders = Physics.OverlapBox(attackArea.position, attackArea.localScale, Quaternion.identity, playerLayer);
            if (hitColliders.Length > 0)
            {
                foreach (var player in hitColliders)
                {
                    PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(player);
                    if (playerControllerB != null)
                    {
                        LogIfDebugBuild("Sacha hit player!");
                        timeSinceHittingLocalPlayer = 0f;
                        playerControllerB.DamagePlayer(30);
                    }
                }
            }
        }


        /***********************
        * AUDIO Utils
        ************************/

        void playRandomSoundFromSounds(AudioClip[] sounds)
        {
            if (sounds == null || sounds.Length == 0) return;
            int index = enemyRandom.Next(0, sounds.Length);
            playSound(sounds[index], true);
        }

        void playSong(){if (!isSongPlaying) playSound(song_SFX, false);}

        // Méthode pour jouer un son à partir d'un AudioClip en local
        void playSound(AudioClip sound, bool useVoice)
        {
            currentAudioClip = sound;
            useVoiceSFX = useVoice;
            PlaySoundFromAudioClientRpc();
        }

        [ClientRpc]
        public void PlaySoundFromAudioClientRpc()
        {
            if (currentAudioClip != null)
            {
                if (useVoiceSFX)
                {
                    creatureVoice.Stop();
                    creatureVoice.PlayOneShot(currentAudioClip);
                }
                else
                {
                    creatureSFX.Stop();
                    creatureSFX.PlayOneShot(currentAudioClip);
                }
            }
        }

        [ClientRpc]
        public void StopSongClientRpc()
        {
            creatureSFX.Stop();
        }
    }
}