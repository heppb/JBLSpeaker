using Photon.Pun;
using System.Collections.Generic;
using UnityEngine;

namespace JBLSpeaker.Valuables
{
    public class JBLSpeaker : MonoBehaviourPun
    {
        private PhysGrabObject grab;
        private bool wasGrabbed;

        public List<AudioSource> tracks;

        [Header("Audio")]
        public List<ParticleSystem> particles;

        private int musicIndex = -1;

        [Header("Behavior")]
        public float slamVelocityThreshold = 6f;
        public int maxSkipsBeforeOff = 6;

        private int skipCount;
        private bool isActive;

        [Header("Drop Audio")]
        public float droppedVolume = 0.25f;
        public float heldVolume = 0.8f;

        private float[] audioSamples = new float[256];
        private float headBobStrength = 0.03f;

        [SerializeField]
        private AudioSource voiceSource;
        [SerializeField] 
        private AudioSource connectedSource;
        [SerializeField] 
        private AudioSource skipSource;
        [SerializeField] 
        private List<AudioSource> musicTracks;

        [SerializeField]
        private List<AudioClip> lyricClips;

        [System.Serializable]
        public class TrackLyrics
        {
            public AudioSource track;
            public List<AudioClip> lyrics;
        }

        [Header("Lyrics")]
        public List<TrackLyrics> trackLyrics;
        public float lyricInterval = 3f;

        private float lyricTimer;
        private AudioSource currentMusic;

        [Header("Voice")]
        [SerializeField] private float voiceCooldown = 1.5f;

        private float lastVoiceTime;

        private void Awake()
        {
            grab = GetComponent<PhysGrabObject>();
            particles = new List<ParticleSystem>(GetComponentsInChildren<ParticleSystem>(true));

            // Find the audio sources automatically
            AudioSource[] allSources = GetComponentsInChildren<AudioSource>(true);

            // Assign by name (must match names in Unity)
            foreach (var src in allSources)
            {
                if (src.gameObject.name == "ConnectedSource")
                    connectedSource = src;
                else if (src.gameObject.name == "SkipSource")
                    skipSource = src;
                else if (src.gameObject.name.StartsWith("Music"))
                    musicTracks.Add(src);
                else if (src.gameObject.name == "VoiceSource")
                    voiceSource = src;
            }

            if (musicTracks.Count == 0)
                Debug.LogError("JBLSpeaker: musicTracks is empty!");
            if (!skipSource)
                Debug.LogError("JBLSpeaker: SkipSource not assigned");

            ShuffleMusic();
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

            foreach (var src in musicTracks)
            {
                if (src.isPlaying)
                {
                    ApplyHeadBob(src);
                    break;
                }
            }
            if (grab.grabbed && currentMusic && currentMusic.isPlaying)
            {
                lyricTimer += Time.deltaTime;
                if (lyricTimer >= lyricInterval)
                {
                    TrySpeakLyricForCurrentTrack();
                    lyricTimer = 0f;
                }
            }
        }

        private void OnPickup()
        {
            isActive = true;
            skipCount = 0;

            RestoreMusicVolume();

            if (!IsAnyMusicPlaying() && connectedSource && connectedSource.clip)
            {
                musicIndex = -1;
                connectedSource.Play();
            }
        }

        private void OnDrop()
        {
            SetMusicVolume(droppedVolume);
            StopParticles(false);
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
            if (!skipSource)
            {
                Debug.LogError("JBLSpeaker: SkipSource not assigned");
                return;
            }

            if (musicTracks == null || musicTracks.Count == 0)
            {
                Debug.LogError("JBLSpeaker: No music tracks assigned");
                return;
            }

            StopMusicOnly();

            if (skipSource.clip)
                skipSource.Play();

            musicIndex++;
            if (musicIndex >= musicTracks.Count)
            {
                ShuffleMusic();
                musicIndex = 0;
            }

            float delay = skipSource.clip ? skipSource.clip.length : 0f;
            Invoke(nameof(PlayCurrentMusic), delay);
        }


        private void PlayCurrentMusic()
        {
            if (!isActive || musicTracks.Count == 0)
                return;

            currentMusic = musicTracks[musicIndex];
            currentMusic.volume = grab.grabbed ? heldVolume : droppedVolume;
            currentMusic.Play();

            foreach (var ps in particles)
                ps.Play();
        }

        private void PowerOff()
        {
            isActive = false;
            StopMusicOnly();
            StopParticles(true);
        }

        private void StopMusicOnly()
        {
            foreach (var src in musicTracks)
                src.Stop();
        }


        private void StopParticles(bool clear = false)
        {
            foreach (var ps in particles)
            {
                if (clear)
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                else
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!grab || !grab.grabbed)
                return;

            if (collision.relativeVelocity.magnitude >= slamVelocityThreshold)
                SkipTrack();
        }

        private void ApplyHeadBob(AudioSource src)
        {
            if (!src || !src.isPlaying)
                return;

            src.GetOutputData(audioSamples, 0);

            float sum = 0f;
            for (int i = 0; i < audioSamples.Length; i++)
                sum += Mathf.Abs(audioSamples[i]);

            float rms = sum / audioSamples.Length;
            float bob = rms * headBobStrength;

            foreach (var player in GameDirector.instance.PlayerList)
            {
                if (player == null) continue;

                float dist = Vector3.Distance(player.transform.position, transform.position);
                if (dist > 8f) continue;

                if (player.isLocal)
                    CameraAim.Instance.AdditiveAimY(bob);
                else
                    player.playerAvatarVisuals.HeadTiltOverride(bob);
            }
        }
        private void TrySpeakLyricForCurrentTrack()
        {
            if (currentMusic == null) return;

            if (trackLyrics == null) return;
            foreach (var entry in trackLyrics)
            {
                if (entry == null) continue;
                if (entry.track == null || entry.lyrics == null) continue;

                if (entry.track == currentMusic && entry.lyrics.Count > 0)
                {
                    var clip = entry.lyrics[Random.Range(0, entry.lyrics.Count)];
                    Speak(clip);
                    return;
                }
            }
        }

        private bool IsAnyMusicPlaying()
        {
            foreach (var src in musicTracks)
                if (src && src.isPlaying)
                    return true;

            return false;
        }
        private void SetMusicVolume(float volume)
        {
            foreach (var src in musicTracks)
                if (src && src.isPlaying)
                    src.volume = volume;
        }

        private void RestoreMusicVolume()
        {
            SetMusicVolume(heldVolume);
        }

        private void Speak(AudioClip clip)
        {
            if (!clip || !voiceSource)
                return;

            if (voiceSource.isPlaying)
                return;

            if (Time.time - lastVoiceTime < voiceCooldown)
                return;

            voiceSource.clip = clip;
            voiceSource.Play();
            lastVoiceTime = Time.time;
        }

        [PunRPC]
        private void RPC_SkipTrack()
        {
            SkipTrack();
        }
    }
}
