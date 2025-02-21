using UnityEditor;
using UnityEngine;

namespace Sacha_Mod
{
    [RequireComponent(typeof(Animator))]
    public class IKControl : MonoBehaviour
    {
        #pragma warning disable 0649
        protected Animator animator = null!;
        public Transform lookObj = null!;
        #pragma warning restore 0649

        public bool ikActive = false;


        void Start()
        {
            // Récupère le GameObject parent
            animator = GetComponent<Animator>();

            Debug.Log("Lezgo :" + animator.name + " type : " + animator.GetType());
        }

        //a callback for calculating IK
        void OnAnimatorIK()
        {
            if (animator)
            {

                //if the IK is active, set the position and rotation directly to the goal.
                if (ikActive)
                {

                    // Set the look target position, if one has been assigned
                    if (lookObj != null)
                    {
                        animator.SetLookAtWeight(1);
                        animator.SetLookAtPosition(lookObj.position);
                    }

                }

                //if the IK is not active, set the position and rotation of the hand and head back to the original position
                else
                {
                    animator.SetLookAtWeight(0);
                }
            }
        }
    }


}
