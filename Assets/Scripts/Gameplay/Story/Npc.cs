using KR.Gameplay.World;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // A character you can talk to. During a Talk milestone that names this character, the milestone's conversation
    // plays; otherwise their everyday chat does.
    public sealed class Npc : MonoBehaviour
    {
        [SerializeField] string id;
        [SerializeField] string displayName;
        [SerializeField] string chatConversation;
        [SerializeField] PersonAnimator animator;

        public string Id => id;
        public string DisplayName => displayName;
        public string ChatConversation => chatConversation;
        public Vector3 HeadPosition => transform.position + Vector3.up * 1.7f;

        public void Configure(string npcId, string name, string chat, PersonAnimator person)
        {
            id = npcId;
            displayName = name;
            chatConversation = chat;
            animator = person;
        }

        public void Talking(bool on, Vector3 towards)
        {
            if (animator != null)
                animator.SetTalking(on, towards);
            else if (on)
            {
                var d = towards - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.LookRotation(d);
            }
        }
    }
}
