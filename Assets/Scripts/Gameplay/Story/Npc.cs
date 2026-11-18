using System.Collections.Generic;
using KR.Gameplay.World;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // A character you can talk to. During a Talk milestone that names this character, the milestone's conversation
    // plays; otherwise their everyday chat does. Staff at a business carry the business name ({place} in lines)
    // and its opening hours: outside them they are not there (ScheduleVisibility on their parent).
    public sealed class Npc : MonoBehaviour
    {
        [SerializeField] string id;
        [SerializeField] string displayName;
        [SerializeField] string chatConversation;
        [SerializeField] PersonAnimator animator;
        [SerializeField, Tooltip("Business this person works at (OSM name), for {place} in lines.")] string place;
        [SerializeField, Tooltip("Talk range override; 0 uses the runner's default.")] float range;

        static readonly List<Npc> Active = new List<Npc>();
        public static IReadOnlyList<Npc> All => Active;

        public string Id => id;
        public string DisplayName => displayName;
        public string ChatConversation => chatConversation;
        public string Place => place;
        public float Range => range;
        public Vector3 HeadPosition => transform.position + Vector3.up * 1.7f;

        public void Configure(string npcId, string name, string chat, PersonAnimator person, string business = null, float talkRange = 0f)
        {
            id = npcId;
            displayName = name;
            chatConversation = chat;
            animator = person;
            place = business;
            range = talkRange;
        }

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

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
