using Photon.Pun;
using System.Collections.Generic;
using UnityEngine;

namespace JBLSpeaker.Valuables
{
    public class JBLSpeaker : MonoBehaviourPun
    {
        private PhysGrabObject grab;
        private bool wasGrabbed;

        [Header("Audio")]
        public List<AudioSource> tracks; // 0 = connected, last = skip
        public List<ParticleSystem> particles;

        private List<AudioSource> musicTracks = new();
        private int musicIndex = -1;

        [Header("Behavior")]
        public float slamVelocityThreshold = 6f;
        public int maxSkipsBeforeOff = 6;

        private int skipCount;
        private bool isActive;

        private void Awake()
        {
            grab = GetComponent<PhysGrabObject>();

            tracks.AddRange(GetComponentsInChildren<AudioSource>(true));
            particles = new List<ParticleSystem>(
                GetComponentsInChildren<ParticleSystem>(true)
            );

            CacheMusicTracks();
        }

        private void Update()
        {
            if (!grab)
                return;

            if (grab.grabbed && !wasGrabbed)
                OnPickup();

            if (!grab.grabbed && wasGrabbed)
                OnDrop();

            wasGrabbed = grab.grabbed;

            if (grab.grabbedLocal && Input.GetKeyDown(KeyCode.E))
                SkipTrack();
        }

        private void OnPickup()
        {
            StopAll();

            skipCount = 0;
            isActive = true;
            musicIndex = -1;

            // Play "connected" once
            tracks[0].Play();
        }

        private void OnDrop()
        {
            StopAll();
        }

        private void CacheMusicTracks()
        {
            musicTracks.Clear();

            // 1 .. Count-2 = music tracks
            for (int i = 1; i < tracks.Count - 1; i++)
                musicTracks.Add(tracks[i]);

            ShuffleMusic();
        }

        private void ShuffleMusic()
        {
            for (int i = musicTracks.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (musicTracks[i], musicTracks[j]) = (musicTracks[j], musicTracks[i]);
            }
        }

        private void SkipTrack()
        {
            if (!isActive)
                return;

            skipCount++;
            if (skipCount >= maxSkipsBeforeOff)
            {
                PowerOff();
                return;
            }

            PlaySkipThenMusic();

            if (photonView.IsMine)
                photonView.RPC(nameof(RPC_SkipTrack), RpcTarget.Others);
        }
        private void PlaySkipThenMusic()
        {
            StopAll();

            // Play skip clip
            tracks[^1].Play();

            // Advance music index
            musicIndex++;
            if (musicIndex >= musicTracks.Count)
            {
                ShuffleMusic();
                musicIndex = 0;
            }

            // Schedule next track after skip finishes
            Invoke(nameof(PlayCurrentMusic), tracks[^1].clip.length);
        }

        private void PlayCurrentMusic()
        {
            if (!isActive)
                return;

            musicTracks[musicIndex].Play();

            foreach (var ps in particles)
                ps.Play();
        }

        private void PowerOff()
        {
            isActive = false;
            StopAll();
        }

        private void StopAll()
        {
            foreach (var src in tracks)
                src.Stop();

            foreach (var ps in particles)
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!grab || !grab.grabbed)
                return;

            if (collision.relativeVelocity.magnitude >= slamVelocityThreshold)
                SkipTrack();
        }

        [PunRPC]
        private void RPC_SkipTrack()
        {
            SkipTrack();
        }
    }
}
