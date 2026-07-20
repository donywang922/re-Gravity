using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Scenes.main_UdonProgramSources
{
    public class StepRingCtrl : UdonSharpBehaviour
    {
        [Header("Particle Systems")]
        public ParticleSystem bigRingParticles;
        public ParticleSystem smallRingParticles;

        [Header("Movement Detection")]
        [Tooltip("玩家落地且水平移动速度达到该值时，视为正在移动。")]
        public float movementSpeedThreshold = 0.2f;

        [Tooltip("玩家在地面移动时生成大环的时间间隔。")]
        public float movingRingInterval = 0.5f;

        [Tooltip("玩家站着不动时生成小环的时间间隔。")]
        public float smallRingInterval = 2.0f;

        private const int InitialPlayerCapacity = 16;
        private const float PlayerPhaseMultiplier = 0.6180339f;
        private VRCPlayerApi[] _players = new VRCPlayerApi[InitialPlayerCapacity];
        private bool _hasLocalGroundState;
        private bool _localWasGrounded;

        public override void PostLateUpdate()
        {
            bool localLandedThisFrame = ProcessLocalLanding();
            int playerCount = VRCPlayerApi.GetPlayerCount();
            if (playerCount > _players.Length)
                _players = new VRCPlayerApi[Mathf.Max(playerCount, _players.Length * 2)];

            VRCPlayerApi.GetPlayers(_players);

            for (int i = 0; i < playerCount; i++)
            {
                VRCPlayerApi player = _players[i];
                if (!Utilities.IsValid(player)) continue;
                if (!player.IsPlayerGrounded()) continue;
                if (localLandedThisFrame && player.isLocal) continue;

                bool moving = GetHorizontalSpeed(player) >= Mathf.Max(0f, movementSpeedThreshold);
                if (moving)
                {
                    if (IsPlayerIntervalTick(player.playerId, movingRingInterval, 0f))
                        EmitAtPosition(bigRingParticles, player.GetPosition());
                }
                else if (IsPlayerIntervalTick(player.playerId, smallRingInterval, 0.5f))
                {
                    EmitAtPosition(smallRingParticles, player.GetPosition());
                }
            }
        }

        private bool ProcessLocalLanding()
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer)) return false;

            bool grounded = localPlayer.IsPlayerGrounded();
            if (!_hasLocalGroundState)
            {
                _hasLocalGroundState = true;
                _localWasGrounded = grounded;
                return false;
            }

            bool landedThisFrame = grounded && !_localWasGrounded;
            _localWasGrounded = grounded;

            if (landedThisFrame)
                EmitAtPosition(bigRingParticles, localPlayer.GetPosition());

            return landedThisFrame;
        }

        private bool IsPlayerIntervalTick(int playerId, float interval, float phaseSalt)
        {
            float safeInterval = Mathf.Max(0.01f, interval);
            float normalizedPhase = Mathf.Repeat(playerId * PlayerPhaseMultiplier + phaseSalt, 1f);
            float phaseOffset = normalizedPhase * safeInterval;
            return Mathf.Repeat(Time.time + phaseOffset, safeInterval) < Mathf.Min(Time.deltaTime, safeInterval);
        }

        private float GetHorizontalSpeed(VRCPlayerApi player)
        {
            Vector3 velocity = player.GetVelocity();
            return new Vector2(velocity.x, velocity.z).magnitude;
        }

        private void EmitAtPosition(ParticleSystem particleSystem, Vector3 playerPosition)
        {
            if (particleSystem == null) return;

            ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams();
            emitParams.position = new Vector3(playerPosition.x, playerPosition.y + 0.001f, playerPosition.z);
            particleSystem.Emit(emitParams, 1);
        }
    }
}
