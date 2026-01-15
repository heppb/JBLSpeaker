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
        public float droppedVolume = 0.5f;
        public float heldVolume = 0.8f;

        [SerializeField] 
        private AudioSource connectedSource;
        [SerializeField] 
        private AudioSource skipSource;
        [SerializeField] 
        private List<AudioSource> musicTracks;

        private Dictionary<AudioSource, string[]> lyricLines;

        private AudioSource currentMusic;
        private enum State
        {
            Idle = 0,
            Active = 1
        }
        private State currentState;
        private float coolDownUntilNextSentence = 3f;
        private string playerName = "{playerName}";
        private PhysGrabObject physGrabObject;

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
            }

            if (musicTracks.Count == 0)
                Debug.LogError("JBLSpeaker: musicTracks is empty!");
            if (!skipSource)
                Debug.LogError("JBLSpeaker: SkipSource not assigned");
        }

        private void Start()
        {
            physGrabObject = GetComponent<PhysGrabObject>();
            GenerateLyrics();
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
                if (SemiFunc.IsMultiplayer())
                {
                    switch (currentState)
                    {
                        case State.Idle:
                            StateIdle();
                            break;
                        case State.Active:
                            StateActive();
                            break;
                    }
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

        private void SkipTrack()
        {
            if (!isActive)
                return;

            if (!HasAuthority())
                return;

            skipCount++;
            if (skipCount >= maxSkipsBeforeOff)
            {
                PowerOff();
                return;
            }

            int nextIndex = GetNextTrackIndex();

            if (SemiFunc.IsMultiplayer())
            {
                photonView.RPC(nameof(RPC_PlayTrackIndex), RpcTarget.All, nextIndex);
            }
            else
            {
                PlayTrackByIndex(nextIndex);
            }
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
            if (!src || !src.isPlaying || !physGrabObject.grabbed)
                return;

            float finalTilt;
            float bpm = 0f;
            bpm = src.name switch
            {
                "Music_01" => 104f,
                "Music_02" => 93f,
                "Music_03" => 128f,
                "Music_04" => 140f,
                _ => 0f,
            };
            if (bpm <= 0f)
            {
                finalTilt = Mathf.Sin(Time.time * 15f) * 25f;
            }
            else
            {
                float beatFreq = bpm / 60f;
                float beatBob = Mathf.Sin(Time.time * Mathf.PI * 2f * beatFreq);
                finalTilt = (beatBob) * 25f;
            }

            foreach (PhysGrabber grabber in physGrabObject.playerGrabbing)
            {
                if (grabber == null)
                    continue;

                if (grabber.isLocal)
                {
                    grabber.playerAvatar.playerExpression.OverrideExpressionSet(4, 100f);
                    PlayerExpressionsUI.instance.playerExpression.OverrideExpressionSet(4, 100f);
                    PlayerExpressionsUI.instance.playerAvatarVisuals.HeadTiltOverride(finalTilt * 0.5f);
                }
                else
                {
                    grabber.playerAvatar.playerAvatarVisuals.HeadTiltOverride(finalTilt);
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
        private void GenerateLyrics()
        {
            Debug.Log("JBLSpeaker: Generate Lyrics");
            lyricLines = new Dictionary<AudioSource, string[]>();

            foreach (var track in musicTracks)
            {
                if (track.name.Contains("Music_01"))
                {
                    //679
                    lyricLines[track] = new[]
                    {
                        "I got a glock in my rari",
                        "I'm like, yeah, she's fine",
                        "Wonder when {playerName} will be mine",
                        "17 shots, no 38",
                        "Seventeen Thirty Eight"
                    };
                }
                else if (track.name.Contains("Music_02"))
                {
                    //Again
                    lyricLines[track] = new[]
                    {
                        "I want you to be mine again baby",
                        "I know my lifestyle is driving you crazy",
                        "Married to the money I aint never letting go",
                        "I go out of the way to see you",
                        "{playerName} I hope you know I need you"
                    };
                }
                else if (track.name.Contains("Music_03"))
                {
                    //My Way
                    lyricLines[track] = new[]
                    {
                        "{playerName} won't you come my way",
                        "Got something I want to say",
                        "Watch me pull out all this dough",
                        "Cannot keep you out my brain",
                        "Flexin on your ex I know"
                    };
                }
                else if (track.name.Contains("Music_04"))
                {
                    //Trapqueen
                    lyricLines[track] = new[]
                    {
                        "She's my trap queen, let her hit the bando",
                        "I'm like hey, what's up? Hello",
                        "Seventeen Thirty Eight ayy",
                        "And I can ride with my baby",
                        "{playerName} can hit it from behind"
                    };
                }
                else
                {
                    // Generic fallback lines
                    lyricLines[track] = new[]
                    {
                        "This song slaps",
                        "Certified banger",
                        "Straight fire",
                        "Seventeen Thirty Eight",
                        "{playerName} is the best"
                    };
                }
            }
        }
        private void StateIdle()
        {
            if (coolDownUntilNextSentence > 0f && physGrabObject.grabbed)
            {
                coolDownUntilNextSentence -= Time.deltaTime;
            }
            else
            {
                if (!PhysGrabber.instance || !PhysGrabber.instance.grabbed || !PhysGrabber.instance.grabbedPhysGrabObject || !(PhysGrabber.instance.grabbedPhysGrabObject == physGrabObject))
                {
                    return;
                }
                bool flag = false;
                if (!SemiFunc.IsMultiplayer())
                {
                    playerName = "JBL Speaker";
                    flag = true;
                }
                else
                {
                    List<PlayerAvatar> list = SemiFunc.PlayerGetAllPlayerAvatarWithinRange(10f, PhysGrabber.instance.transform.position);
                    PlayerAvatar playerAvatar = null;
                    float num = float.MaxValue;
                    foreach (PlayerAvatar item in list)
                    {
                        if (!(item == PlayerAvatar.instance))
                        {
                            float num2 = Vector3.Distance(PhysGrabber.instance.transform.position, item.transform.position);
                            if (num2 < num)
                            {
                                num = num2;
                                playerAvatar = item;
                            }
                        }
                    }
                    flag = true;
                    if (playerAvatar != null)
                    {
                        playerName = playerAvatar.playerName;
                    }
                    else
                    {
                        playerName = "JBL Speaker";
                    }
                }
                if (flag)
                {
                    string message = TrySpeakLyricForCurrentTrack();
                    if (message != null)
                    {
                        currentState = State.Active;
                        Color possessColor = new Color(1f, 0.3f, 0.6f, 1f);
                        ChatManager.instance.PossessChatScheduleStart(10);
                        ChatManager.instance.PossessChat(ChatManager.PossessChatID.LovePotion, message, 1f, possessColor);
                        ChatManager.instance.PossessChatScheduleEnd();
                    }                    
                }
            }
        }

        private void StateActive()
        {
            if (PhysGrabber.instance.grabbed && (bool)PhysGrabber.instance.grabbedPhysGrabObject && PhysGrabber.instance.grabbedPhysGrabObject != physGrabObject)
            {
                currentState = State.Idle;
                coolDownUntilNextSentence = Random.Range(15f, 30f);
            }
            else if (!ChatManager.instance.StateIsPossessed())
            {
                currentState = State.Idle;
                coolDownUntilNextSentence = Random.Range(15f, 30f);
            }
        }

        private string TrySpeakLyricForCurrentTrack()
        {
            if (!currentMusic || !currentMusic.isPlaying)
                return null;
            if (!lyricLines.ContainsKey(currentMusic))
                return null;
            var lines = lyricLines[currentMusic];
            if (lines.Length == 0)
                return null;

            string line = lines[Random.Range(0, lines.Length)];
            line= line.Replace("{playerName}", playerName);
            return line;
        }

        private void PlayTrackByIndex(int index)
        {
            if (index < 0 || index >= musicTracks.Count)
                return;

            StopMusicOnly();

            musicIndex = index;
            currentMusic = musicTracks[musicIndex];
            currentMusic.volume = grab.grabbed ? heldVolume : droppedVolume;
            currentMusic.Play();

            foreach (var ps in particles)
                ps.Play();
        }

        private int GetNextTrackIndex()
        {
            if (musicTracks == null || musicTracks.Count == 0)
                return -1;

            int next = musicIndex + 1;
            if (next >= musicTracks.Count)
                next = 0;

            return next;
        }

        private bool HasAuthority()
        {
            if (!SemiFunc.IsMultiplayer())
                return true;

            if (!photonView)
                return false;

            // Prefer ownership, fallback to master
            return photonView.IsMine || PhotonNetwork.IsMasterClient;
        }

        [PunRPC]
        private void RPC_PlayTrackIndex(int index)
        {
            PlayTrackByIndex(index);
        }
    }
}
