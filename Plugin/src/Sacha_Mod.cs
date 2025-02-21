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
#pragma warning restore 0649
        Vector3 directionAway;
        Transform farestNodeFromPos = null!;
        float timeSinceHittingLocalPlayer;
        float attackDelay;
        float attackedDelay;
        float randomSoundDelay;
        float animationDelay;
        bool isAnimationPlaying;
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
        float maxTargetDistance = 15f;
        bool isStaminaFilling;
        float backupDistance = 3f;
        float idleDistance = 4f;
        bool updateRotation = true;
        float runDistance = 6f;  // Seuil de distance à partir duquel l'ennemi court
        enum State
        {
            SearchingForPlayer,
            FollowPlayer,
            IsInCombat,
            RunAway,
            AttackInProgreess,
            ReceiveHitInProgress,
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
                }
                return;
            }

            timeSinceHittingLocalPlayer += Time.deltaTime;
            attackDelay += Time.deltaTime;
            randomSoundDelay += Time.deltaTime;
            if (currentBehaviourStateIndex == (int)State.IsInCombat)
            {
                attackedDelay += Time.deltaTime;
            }

            var state = currentBehaviourStateIndex;
            if (targetPlayer != null && state != (int)State.AttackInProgreess)
            {
                view.transform.position = targetPlayer.gameplayCamera.transform.position;
                if (updateRotation)
                {
                    turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
                    transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 4f * Time.deltaTime);
                }
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
            isWalking = agent.velocity.magnitude > 0.1 && !isRunning;

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
                case (int)State.AttackInProgreess:
                    // We don't care about doing anything here
                    break;
                case (int)State.ReceiveHitInProgress:
                    break;
                case (int)State.HelloInProgress:
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
                if (!isAnimationPlaying && tryDoRandomAnimation())
                {
                    agent.speed = 0;
                    StopSearch(currentSearch);
                    isAnimationPlaying = true;
                }
                else if (isAnimationPlaying && animationDelay >= 10f)
                {
                    agent.speed = walkSpeed;
                    StartSearch(transform.position);
                    ResetAnimation();
                }
                animationDelay += 0.1f;
            }

            TryPlayRandomSound();

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
                        if(enemyRandom.Next(0, 3) == 0){
                            playSong();
                        }
                        SwitchToBehaviourClientRpc((int)State.FollowPlayer);
                    }
                }
                targetPosition = transform.position;
                SetDestinationToPosition(targetPosition, checkForPath: false);
                StopSearch(currentSearch);
            }
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
                if (!tryAttack(3))
                {
                    StickingInFrontOfPlayer();
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

            // Keep targeting closest player, unless they are over 20 units away and we can't see them.
            if (isAnimationPlaying)
            {
                animationDelay += 0.1f;
                if (animationDelay >= 20f || Vector3.Distance(transform.position, targetPlayer.transform.position) > runDistance)
                {
                    ResetAnimation();
                }
                return;
            }

            if (tryDoRandomAnimation())
            {
                isAnimationPlaying = true;
                targetPosition = transform.position;
                SetDestinationToPosition(targetPosition, checkForPath: false);
            }

            if (!reactToEmote())
            {
                creatureAnimator.SetBool("isMarteloAttack", true);
                if (!tryAttack(6))
                {
                    StickingInFrontOfPlayer();
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
                LogIfDebugBuild("Run away succeed !");
                StartSearch(transform.position);
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                creatureAnimator.SetBool("isCombat", false);
                DoAnimationClientRpc("stopRunAway");
                updateRotation = true;
                ik_Control.ikActive = true;
            }
        }

        private void ResetAnimation()
        {
            animationDelay = 0;
            isAnimationPlaying = false;
            DoAnimationClientRpc("stopEmote");
        }


        private void TryPlayRandomSound()
        {
            if (randomSoundDelay > 10f && !isVoiceSoundPlaying())
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
            if (targetPlayer == null)
            {
                return false;
            }

            float distanceToPlayer = Vector3.Distance(transform.position, targetPlayer.transform.position);
            if (distanceToPlayer > maxTargetDistance && (!TargetClosestPlayerInAnyCase() || !CheckLineOfSightForPosition(targetPlayer.gameplayCamera.transform.position)))
            {
                LogIfDebugBuild("Stop Target Player");
                StartSearch(transform.position);
                creatureAnimator.SetBool("isCombat", false);
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                playSoundFromAudio(stopTarget_SFX, true);
                StartCoroutine(FadeOutAudioSFX(0.5f));
                return false;
            }
            return true;
        }

        IEnumerator FadeOutAudioSFX(float fadeDuration)
        {
            float startVolume = creatureSFX.volume;

            while (creatureSFX.volume > 0)
            {
                creatureSFX.volume -= startVolume * Time.deltaTime / fadeDuration;
                yield return null;
            }

            creatureSFX.Stop();
            creatureSFX.volume = startVolume;
        }


        void playRandomSoundFromSounds(AudioClip[] sounds)
        {
            if (sounds == null || sounds.Length == 0)
            {
                return;
            }
            int index = enemyRandom.Next(0, sounds.Length);
            playSoundFromAudio(sounds[index],true);
        }

        void playSong()
        {
            if (!isSongPlaying)
            {
                playSoundFromAudio(song_SFX, false);
            }
        }

        void playSoundFromAudio(AudioClip sound, bool useVoiceSFX)
        {
            if (useVoiceSFX)
            {
                creatureVoice.Stop();
                creatureVoice.PlayOneShot(sound);
            }
            else
            {
                creatureSFX.Stop();
                creatureSFX.PlayOneShot(sound);
            }
        }

        bool tryAttack(int chance)
        {
            if (attackDelay > 2f)
            {
                if (enemyRandom.Next(0, chance) == 0)
                {
                    // Attack
                    StartCoroutine(attack());
                    return true;
                }
                attackDelay = 0;
            }
            return false;
        }

        bool reactToEmote()
        {
            if (targetPlayer.performingEmote)
            {
                if (!hasClaped && !isRunning && !isWalking)
                {
                    hasClaped = true;
                    StartCoroutine(clap());
                    return true;
                }
            }
            else
            {
                hasClaped = false;
            }
            return false;
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

        void StickingInFrontOfPlayer()
        {
            if (targetPlayer == null || !IsOwner)
            {
                return;
            }

            // Rotation fluide vers le joueur
            turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
            transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 4f * Time.deltaTime);

            // Calcul de la distance entre l'ennemi et le joueur
            float distanceToPlayer = Vector3.Distance(transform.position, targetPlayer.transform.position);

            // Logique de déplacement de l'ennemi
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
            yield return new WaitForSeconds(0.5f);
            playRandomSoundFromSounds(clap_SFX);

            DoAnimationClientRpc("clap");

            yield return new WaitUntil(() =>
            !creatureAnimator.GetCurrentAnimatorStateInfo(0).IsName("clap") ||
            creatureAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1.0f
            );
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
                yield break;
            }

            SwitchToBehaviourClientRpc((int)State.AttackInProgreess);
            agent.speed = 5f;
            float distanceToPlayer = Vector3.Distance(transform.position, targetPlayer.transform.position);
            // Tant que l'attaque ne doit pas être lancée, on continue à suivre le joueur
            while (distanceToPlayer > 1f)
            {
                if(distanceToPlayer > runDistance)
                {
                    if (attackedDelay != 0)
                    {
                        SwitchToBehaviourClientRpc((int)State.IsInCombat);
                    }
                    else
                    {
                        SwitchToBehaviourClientRpc((int)State.FollowPlayer);
                    }
                    yield break;
                }
                // Mise à jour dynamique de la position cible
                Vector3 direction = (transform.position - targetPlayer.transform.position).normalized;
                targetPosition = targetPlayer.transform.position + direction * 0.5f;
                SetDestinationToPosition(targetPosition);
                distanceToPlayer = Vector3.Distance(transform.position, targetPlayer.transform.position);
                yield return null; // Attendre le prochain frame pour recalculer la position
            }

            if(Vector3.Distance(transform.position, targetPlayer.transform.position) > 2f)
            {
                DoAnimationClientRpc("attack");
                // Attendre que l'animation soit réellement terminée (optionnel si la durée est bien synchronisée)
                yield return null;
                yield return new WaitUntil(() =>
                    !(creatureAnimator.GetCurrentAnimatorStateInfo(0).IsName("Hit") ||
                    creatureAnimator.GetCurrentAnimatorStateInfo(0).IsName("martelo")) ||
                    creatureAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1.0f
                );


                // Appliquer les dégâts au joueur
                attackHitClientRpc();
            }

            if (attackedDelay != 0)
            {
                SwitchToBehaviourClientRpc((int)State.IsInCombat);
            }
            else
            {
                SwitchToBehaviourClientRpc((int)State.FollowPlayer);
            }
        }

        IEnumerator TakeHit(int force)
        {
            SwitchToBehaviourClientRpc((int)State.ReceiveHitInProgress);
            StartCoroutine(FadeOutAudioSFX(0.5f));
            ik_Control.ikActive = false;
            // Jouer l'animation de prise de coup
            creatureAnimator.SetInteger("hitIndex", enemyRandom.Next(0, 2));
            DoAnimationClientRpc("hit");
            playRandomSoundFromSounds(hit_SFX);

            if (enemyHP - force <= 0)
            {
                StopCoroutine(attack());
                SwitchToBehaviourClientRpc((int)State.RunAway);
                creatureAnimator.SetBool("isCombat", false);
                playSoundFromAudio(flee_SFX,true);
                DoAnimationClientRpc("runAway");
                updateRotation = false;
                farestNodeFromPos = ChooseFarthestNodeFromPosition(targetPlayer.transform.position);
                targetPosition = farestNodeFromPos.position;
                SetDestinationToPosition(targetPosition, checkForPath: true);
                directionAway = (transform.position - targetPlayer.transform.position).normalized;
                agent.speed = runSpeed * 3f;
                ik_Control.ikActive = false;
            }
            else
            {
                // Attendre que l'animation soit terminée avant de continuer
                SwitchToBehaviourClientRpc((int)State.IsInCombat);
                creatureAnimator.SetBool("isCombat", true);
                attackedDelay = 0f;
                ik_Control.ikActive = true;
                yield return new WaitForSeconds(0.5f);
                playRandomSoundFromSounds(combat_SFX);
            }
            yield break;
        }


        bool IsPlayerLookingAtEnemy()
        {
            if (targetPlayer == null) return false;

            Vector3 directionToEnemy = (transform.position - targetPlayer.gameplayCamera.transform.position).normalized;
            float dotProduct = Vector3.Dot(targetPlayer.gameplayCamera.transform.forward, directionToEnemy);

            // Vérifier si le joueur regarde l'ennemi (angle correct)
            if (dotProduct > 0.50f)
            {
                return CheckLineOfSightForPosition(targetPlayer.gameplayCamera.transform.position);
            }
            return false;
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            if (timeSinceHittingLocalPlayer < 1f || currentBehaviourStateIndex == (int)State.RunAway || currentBehaviourStateIndex == (int)State.AttackInProgreess)
            {
                return;
            }
            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
            if (playerControllerB != null)
            {
                LogIfDebugBuild("Sacha Collision with Player!");
                timeSinceHittingLocalPlayer = 0f;
                creatureAnimator.SetBool("isMarteloAttack", false);
                DoAnimationClientRpc("attack");
                playRandomSoundFromSounds(attack_SFX);
                playerControllerB.DamagePlayer(20);
            }
        }

        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if (isEnemyDead)
            {
                return;
            }
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
            if (enemyRandom.Next(0, 100) == 0)
            {
                creatureAnimator.SetInteger("randomEmote", enemyRandom.Next(0, 4));
                DoAnimationClientRpc("startEmote");
                isAnimationPlaying = true;
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
            LogIfDebugBuild("attackHitClientRPC");
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
                        playerControllerB.DamagePlayer(20);
                    }
                }
            }
        }
    }
}