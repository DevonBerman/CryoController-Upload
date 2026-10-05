using UnityEngine;

namespace CryoController.Demo
{
    /// <summary>
    /// Demo only: a detonator. Using it throws the loose rigidbodies around Blast Point and shakes the player's camera,
    /// harder the closer they stand, with <see cref="FirstPersonController.AddCameraShake"/>.
    /// </summary>
    [AddComponentMenu("CryoController/Demo/Demo Blast Button")]
    public class DemoBlastButton : Interactable
    {
        [Tooltip("Where the blast goes off. Empty uses this object.")]
        [SerializeField] private Transform blastPoint;
        [Tooltip("Loose rigidbodies within this distance of the blast, in metres, are thrown.")]
        [SerializeField, Min(0f)] private float blastRadius = 5f;
        [Tooltip("Impulse of the blast at its centre, in newton seconds; it falls off to nothing at Blast Radius.")]
        [SerializeField, Min(0f)] private float blastImpulse = 6f;
        [Tooltip("The camera shakes fully at the blast and not at all from this far away, in metres.")]
        [SerializeField, Min(0.1f)] private float shakeRadius = 15f;

        private readonly Collider[] hits = new Collider[32];

        protected override void OnInteract(PlayerInteractor interactor)
        {
            Vector3 centre = blastPoint != null ? blastPoint.position : transform.position;

            // Carried objects sit on the Ignore Raycast layer, which the default layers leave out: they stay in hand.
            int count = Physics.OverlapSphereNonAlloc(centre, blastRadius, hits, Physics.DefaultRaycastLayers,
                                                      QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = hits[i].attachedRigidbody;
                if (body != null && !body.isKinematic && hits[i] == FirstCollider(body, i))
                {
                    body.AddExplosionForce(blastImpulse, centre, blastRadius, 0.5f, ForceMode.Impulse);
                }
            }

            FirstPersonController player = interactor.GetComponent<FirstPersonController>();
            if (player != null)
            {
                float distance = Vector3.Distance(player.transform.position, centre);
                player.AddCameraShake(Mathf.Clamp01(1f - distance / shakeRadius));
            }
        }

        // The first of this body's colliders among the hits, so a body with several colliders is thrown only once.
        private Collider FirstCollider(Rigidbody body, int upTo)
        {
            for (int i = 0; i < upTo; i++)
            {
                if (hits[i].attachedRigidbody == body)
                {
                    return hits[i];
                }
            }
            return hits[upTo];
        }
    }
}
