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
    
        [Header("Ground Parameters")]
        public float groundY = 0f;
        public float triggerThreshold = 0.05f;
        public float smallRingInterval = 2.0f;
        public float footCloseThreshold = 0.25f;
    
        // 设为32以应对16人房间在人员轮替、新老玩家过渡时的边缘情况
        private const int SlotCount = 32; 
        private readonly VRCPlayerApi[] _players = new VRCPlayerApi[SlotCount];
    
        // 用于状态跟踪的并行数组
        private readonly int[] _slottedPlayerIds = new int[SlotCount];
        private readonly bool[] _leftFootDown = new bool[SlotCount];
        private readonly bool[] _rightFootDown = new bool[SlotCount];
        private readonly float[] _leftTimer = new float[SlotCount];
        private readonly float[] _rightTimer = new float[SlotCount];

        void Start()
        {
            // 初始化所有槽位为无效ID
            for (int i = 0; i < SlotCount; i++)
            {
                _slottedPlayerIds[i] = -1;
            }
        }

        public override void PostLateUpdate()
        {
            int playerCount = VRCPlayerApi.GetPlayerCount();
            // 获取当前房间内的所有活跃玩家
            VRCPlayerApi.GetPlayers(_players);

            for (int i = 0; i < playerCount; i++)
            {
                VRCPlayerApi player = _players[i];
                if (!Utilities.IsValid(player)) continue;

                // 获取该玩家固定的内部数据槽位索引
                int slot = GetPlayerSlot(player.playerId);
                if (slot == -1) continue;

                // 获取骨骼位置：优先获取脚趾（Toes）高度，若无脚趾则获取脚部（Foot）高度，以适配高跟鞋等特殊模型
                Vector3 leftToePos = player.GetBonePosition(HumanBodyBones.LeftToes);
                Vector3 leftFootPos = (leftToePos != Vector3.zero) ? leftToePos : player.GetBonePosition(HumanBodyBones.LeftFoot);

                Vector3 rightToePos = player.GetBonePosition(HumanBodyBones.RightToes);
                Vector3 rightFootPos = (rightToePos != Vector3.zero) ? rightToePos : player.GetBonePosition(HumanBodyBones.RightFoot);

                bool leftValid = leftFootPos != Vector3.zero;
                bool rightValid = rightFootPos != Vector3.zero;

                if (leftValid && rightValid)
                {
                    float dist = Vector3.Distance(leftFootPos, rightFootPos);
                    if (dist < footCloseThreshold)
                    {
                        // 两个脚如果很近应该只出一个波纹，压制右脚的波纹发射
                        ProcessFoot(leftFootPos, slot, true, false);
                        ProcessFoot(rightFootPos, slot, false, true);
                    }
                    else
                    {
                        ProcessFoot(leftFootPos, slot, true, false);
                        ProcessFoot(rightFootPos, slot, false, false);
                    }
                }
                else
                {
                    if (leftValid)
                    {
                        ProcessFoot(leftFootPos, slot, true, false);
                    }
                    if (rightValid)
                    {
                        ProcessFoot(rightFootPos, slot, false, false);
                    }
                }
            }
        }

        private int GetPlayerSlot(int playerId)
        {
            // 1. 尝试查找已分配的现有槽位
            for (int i = 0; i < SlotCount; i++)
            {
                if (_slottedPlayerIds[i] == playerId) return i;
            }

            // 2. 若未找到，查找空闲槽或已离线玩家留下的过期槽进行复用
            for (int i = 0; i < SlotCount; i++)
            {
                if (_slottedPlayerIds[i] == -1)
                {
                    InitializeSlot(i, playerId);
                    return i;
                }

                VRCPlayerApi cachedPlayer = VRCPlayerApi.GetPlayerById(_slottedPlayerIds[i]);
                if (!Utilities.IsValid(cachedPlayer))
                {
                    InitializeSlot(i, playerId);
                    return i;
                }
            }

            return -1;
        }

        private void InitializeSlot(int slot, int playerId)
        {
            _slottedPlayerIds[slot] = playerId;
            _leftFootDown[slot] = false;
            _rightFootDown[slot] = false;
            _leftTimer[slot] = 0f;
            _rightTimer[slot] = 0f;
        }

        private void ProcessFoot(Vector3 footPos, int slot, bool isLeft, bool suppressEmit)
        {
            bool currentlyDown = footPos.y <= (groundY + triggerThreshold);
        
            bool isDown = isLeft ? _leftFootDown[slot] : _rightFootDown[slot];
            float timer = isLeft ? _leftTimer[slot] : _rightTimer[slot];

            if (currentlyDown && !isDown)
            {
                // 刚刚踩下地面：若未压制则发射大圆环
                if (!suppressEmit)
                {
                    EmitAtPosition(bigRingParticles, footPos);
                }

                // 左右脚波纹应该错开时间：如果另一只脚已经踩在地面上，将当前脚的计时器设置为间隔的一半，以错开小圆环的发射时间
                bool otherDown = isLeft ? _rightFootDown[slot] : _leftFootDown[slot];
                if (otherDown)
                {
                    timer = smallRingInterval * 0.5f;
                }
                else
                {
                    timer = 0f;
                }
            }
            else if (currentlyDown)
            {
                // 持续停留在地面上：累计时间并偶尔发射小圆环
                timer += Time.deltaTime;
                if (timer >= smallRingInterval)
                {
                    if (!suppressEmit)
                    {
                        EmitAtPosition(smallRingParticles, footPos);
                    }
                    timer = 0f;
                }
            }
            else
            {
                timer = 0f;
            }

            // 保存状态到对应的槽位
            if (isLeft)
            {
                _leftFootDown[slot] = currentlyDown;
                _leftTimer[slot] = timer;
            }
            else
            {
                _rightFootDown[slot] = currentlyDown;
                _rightTimer[slot] = timer;
            }
        }

        private void EmitAtPosition(ParticleSystem ps, Vector3 pos)
        {
            if (ps == null) return;

            ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams();
            emitParams.position = new Vector3(pos.x, groundY + 0.001f, pos.z);
            ps.Emit(emitParams, 1);
        }
    }
}