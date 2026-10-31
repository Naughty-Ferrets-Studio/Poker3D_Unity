using System;
using UnityEngine;

namespace HoldemTable
{
    /// <summary>
    /// A placeholder player: a flat horizontal ellipse (a squashed ball) behind the seat. It shakes by the seat's tell level,
    /// so opponents can read Disguised Card, Bumba Card and bluffs. Swap it for a real model later: only Update() cares about the shake.
    /// </summary>
    public class SeatAvatar : MonoBehaviour
    {
        public int Seat;
        public Func<int, float> TellLevel;   // 0 = calm, 1 = shaking all the time

        private Vector3 _basePos;
        private Quaternion _baseRot;
        private float _seed;

        private void Start()
        {
            _basePos = transform.localPosition;
            _baseRot = transform.localRotation;
            _seed = UnityEngine.Random.value * 100f + Seat * 17f;
        }

        private void Update()
        {
            float level = TellLevel != null ? Mathf.Clamp01(TellLevel(Seat)) : 0f;
            if (level <= 0.001f)
            {
                transform.localPosition = _basePos;
                transform.localRotation = _baseRot;
                return;
            }
            // shaking comes in bursts: a high level is almost always shaking, a low level only now and then
            float t = Time.time;
            float burst = Mathf.PerlinNoise(_seed + t * 1.3f, 0.5f);
            float gate = Mathf.SmoothStep(0f, 1f, (burst - (0.75f - 0.6f * level)) / 0.15f);
            float amp = level * gate * 0.12f;

            float x = (Mathf.PerlinNoise(_seed + t * 28f, 1.5f) - 0.5f) * 2f * amp;
            float z = (Mathf.PerlinNoise(_seed + t * 31f, 2.5f) - 0.5f) * 2f * amp;
            float roll = (Mathf.PerlinNoise(_seed + t * 26f, 3.5f) - 0.5f) * 2f * amp * 120f;
            transform.localPosition = _basePos + new Vector3(x, 0f, z);
            transform.localRotation = _baseRot * Quaternion.Euler(0f, 0f, roll);
        }
    }
}
