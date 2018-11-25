//TODO：优化结构

/*******
 * ［概述］
 * 
 * 
 * ［用法］
 * 挂载到EnemyContainer上
 * 
 * ［备注］ 
 * 
 * 项目：《黑暗之魂》复刻教程
 * 作者：微风的龙骑士 风游迩
 */


using System.Collections;
using DSWork.Global;
using DSWork.Utility;
using UnityEngine;

namespace DSWork.Enemy {
	/// <summary>动作控制器</summary>
	public class DummyActionCtrl : MonoBehaviour {
		#region ［字段和属性］

		/// <summary>移动速度</summary>
		public float walkSpeed = 2f;
		/// <summary>奔跑速度倍率</summary>
		public float runMultiplier = 2.0f;

		/// <summary>翻滚速度</summary>
		public float rollSpeed_Z = 3f;
		/// <summary>跳跃速度</summary>
		public float jumpSpeed_Z = 2f;

		[Space(10)]
		[Header("===Friction Settings===")]
		public PhysicMaterial frictionOne;
		public PhysicMaterial frictionZero;

		/// <summary>重力加速度</summary>
		private const float gravity = 9.8f;
		private float fallTime;

		private GameObject model;
		private Animator animator;
		private CharacterController cc;
		private DummyInputMgr pi;
		private EnemyCameraCtrl cameraCtrl;

		/// <summary>锁死平面移动</summary>
		private bool lockMove;
		/// <summary>锁死攻击</summary>
		private bool lockAction;
		/// <summary>追踪方向</summary>
		private bool trackDir;

		/// <summary>平面移动速度向量</summary>
		private Vector3 planeMoveVector = Vector3.zero;
		/// <summary>附加向量</summary>
		private Vector3 thrustVector = Vector3.zero;
		/// <summary>Root Motion相关（攻击的第三下要向前移动一段距离）</summary>
		private Vector3 deltaVector;

		/// <summary>插值目标，用于转换FSM层级</summary>
		private float layerWeightTarget;

		public bool useShield_LHand = true;
		/// <summary>是否使用双手（例如：在举盾时可以攻击）</summary>
		public bool useBothHand;

		#endregion


		#region ［Unity消息］

		private void Awake() {
			//得到组件
			model = GameObject.Find("TestEnemy");
			animator = model.GetComponent<Animator>();
			cc = model.GetComponent<CharacterController>();
			pi = model.GetComponent<DummyInputMgr>();
			cameraCtrl = model.GetComponent<EnemyCameraCtrl>();

			RegisterFSMEvents();

			//控制Root Motion
			model.GetComponent<RootMotionControl>().RMUpdateEvent += OnRMUpdate;
		}

		private void OnEnable() {
			StartCoroutine(PlayerActionCr());
		}

		private void OnDisable() {
			StopCoroutine(PlayerActionCr());
		}

		#endregion


		#region ［角色控制方法］

		/// <summary>角色的动作协程</summary>
		/// <returns></returns>
		private IEnumerator PlayerActionCr() {
			yield return new WaitForEndOfFrame();
			while(true) {
				PlayerAction();
				PlayerMove();
				UseGravity();
				PlayerFallRoll();
				OnGroundCheck();
				yield return new WaitForSeconds(0.02f);
			}
		}


		/// <summary>角色的移动控制，整体的速度控制</summary>
		private void PlayerMove() {
			//设置玩家锁定的相关参数
			if(!cameraCtrl.LockState)
				SetPlayerMove();
			else
				SetPlayerMove_Locked();

			//玩家移动（考虑跳跃、翻滚等动作带有的位移偏量，以及攻击工作带有的位移偏量）
			cc.SimpleMove((planeMoveVector + thrustVector + deltaVector) * Time.deltaTime);
			//重置向量参数
			planeMoveVector = Vector3.zero;
			thrustVector = Vector3.zero;
			deltaVector = Vector3.zero;
		}


		/// <summary>设置玩家移动的相关参数</summary>
		private void SetPlayerMove() {
			//设置FSM混合参数
			animator.SetFloat(EPlayer_FSMParam.forward.TS(), pi.Distance);
			animator.SetFloat(EPlayer_FSMParam.right.TS(), 0);
			//设置方向（旋转，使用球面线性插值，用于变换向量）
			//如果玩家做了移动，则平滑改变玩家的朝向
			if(pi.Distance > 0.1f)
				model.transform.forward = Vector3.Slerp(model.transform.forward, pi.Direction, 0.5f);
			//设置玩家速度（二维）（考虑是否奔跑）（考虑是否锁定了平面移动）
			//NOTE：只考虑玩家的移动距离，而玩家的移动方向/朝向另做考虑，参照的移动方向始终为正向
			if(!lockMove) {
				planeMoveVector = model.transform.forward * pi.Distance * walkSpeed * pi.Sgn_Run.ToN_1(runMultiplier);
				planeMoveVector = new Vector3(planeMoveVector.x, 0, planeMoveVector.z);
			}
		}


		/// <summary>设置玩家移动的相关参数（锁定状态下）</summary>
		private void SetPlayerMove_Locked() {
			//设置FSM混合参数
			animator.SetFloat(EPlayer_FSMParam.forward.TS(), pi.Forward * pi.Sgn_Run.To2_1());
			animator.SetFloat(EPlayer_FSMParam.right.TS(), pi.Right * pi.Sgn_Run.To2_1());
			//设置方向（旋转）
			//NOTE：如果不需要追踪方向，则以相机的正方向为准，否则以人物移动的正方向为准
			if(!trackDir)
				model.transform.forward = cameraCtrl.CameraForward;
			else
				model.transform.forward = pi.Direction.normalized;
			//设置玩家速度（二维）
			//NOTE：移动距离和移动方向相对于摄像机
			if(!lockMove)
				planeMoveVector = pi.Direction * walkSpeed * pi.Sgn_Run.ToN_1(runMultiplier);
		}


		/// <summary>角色的动作控制</summary>
		private void PlayerAction() {
			//锁定
			if(pi.Sgn_Lock)
				cameraCtrl.ToggleLock();

//			//跳跃
//			if(pi.Sgn_Jump)
//				animator.SetTrigger(EPlayer_FSMParam.jump.TS());
//			//闪避
//			else if(pi.Sgn_Dodge)
//				animator.SetTrigger(EPlayer_FSMParam.dodge.TS());



			//设置角色的左右手动作
			//NOTE：注意动作的优先级
//			//攻击
//			if(pi.Sgn_RHandAct1 && animator.CheckState(PlayerFSMState.OnGround) || animator.CheckStateTag(PlayerFSMStateTag.Attack))
			animator.SetBool(EPlayer_FSMParam.defense.TS(), false);
			if(lockAction) {
				if(pi.Sgn_RHandAct2) {
					animator.SetBool(EPlayer_FSMParam.useLeftHand.TS(), false);
				}
				else if(pi.Sgn_RHandAct1) {
					animator.SetBool(EPlayer_FSMParam.useLeftHand.TS(), false);
					animator.SetTrigger(EPlayer_FSMParam.attack.TS());
				}

				if(pi.Sgn_LHandAct2) {
					animator.SetBool(EPlayer_FSMParam.useLeftHand.TS(), true);

				}
				else if(pi.Sgn_LHandAct1) {
					animator.SetBool(EPlayer_FSMParam.useLeftHand.TS(), true);
					//NOTE：只能在着地状态下防御
					if(useShield_LHand)
						animator.SetBool(EPlayer_FSMParam.defense.TS(), true);
					else
						animator.SetTrigger(EPlayer_FSMParam.attack.TS());
				}
			}
		}


		/// <summary>角色的下落翻滚控制</summary>
		/// <remarks>如果下落速度很快，则需要在落地时进行一次翻滚</remarks>
		private void PlayerFallRoll() {
			if(cc.velocity.magnitude >= 5.0f)
				animator.SetTrigger(EPlayer_FSMParam.fallRoll.TS());
		}


		/// <summary>角色的重力控制</summary>
		private void UseGravity() {
			if(!cc.isGrounded) {
				fallTime += Time.deltaTime;
				cc.Move(Vector3.down * 1 / 2 * gravity * fallTime * Time.deltaTime);
			}
			else if(cc.isGrounded && fallTime <= 0) {
				fallTime = 0;
			}
		}

		/// <summary>着地检查</summary>
		public void OnGroundCheck() {
			if(!animator.GetBool(EPlayer_FSMParam.isOnGround.TS()) && cc.isGrounded)
				animator.SetBool(EPlayer_FSMParam.isOnGround.TS(), true);
			else if(animator.GetBool(EPlayer_FSMParam.isOnGround.TS()) && !cc.isGrounded)
				animator.SetBool(EPlayer_FSMParam.isOnGround.TS(), false);
		}

		#endregion


		#region ［FSM事件方法 仍然需要完善］

		/// <summary>注册FSM事件</summary>
		private void RegisterFSMEvents() {
			//后跃状态的相关事件
			animator.GetBehaviour<FSMEvents_Jab>().OnEnterEvent += () => {
				StateLock();
				trackDir = true;
			};
			animator.GetBehaviour<FSMEvents_Jab>().OnExitEvent += StateUnlock;
			animator.GetBehaviour<FSMEvents_Jab>().OnUpdateEvent += () =>
				StateSetSpeed(EPlayer_FSMCurve.spd_Jab_Y, EPlayer_FSMCurve.spd_Jab_Z);

			//跳跃状态的相关事件
			animator.GetBehaviour<FSMEvents_Jump>().OnEnterEvent += () => {
				StateLock();
				trackDir = true;
			};
			animator.GetBehaviour<FSMEvents_Jump>().OnUpdateEvent += () =>
				StateSetSpeed(EPlayer_FSMCurve.spd_Jump_Y, jumpSpeed_Z);
			animator.GetBehaviour<FSMEvents_Jump>().OnExitEvent += StateUnlock;

			//翻滚状态的相关事件
			animator.GetBehaviour<FSMEvents_Roll>().OnEnterEvent += () => {
				StateLock();
				trackDir = true;
			};
			animator.GetBehaviour<FSMEvents_Roll>().OnUpdateEvent += () =>
				StateSetSpeed(EPlayer_FSMCurve.spd_Roll_Y, rollSpeed_Z);
			animator.GetBehaviour<FSMEvents_Roll>().OnExitEvent += StateUnlock;

			//掉落状态的相关事件
			animator.GetBehaviour<FSMEvents_Fall>().OnEnterEvent += StateUnlock;

			//着地状态的相关事件
			animator.GetBehaviour<FSMEvents_OnGround>().OnEnterEvent += () => {
				StateUnlock();
				trackDir = false;
				cc.material = frictionOne;
			};
			animator.GetBehaviour<FSMEvents_OnGround>().OnExitEvent += () => { cc.material = frictionZero; };


			//攻击层级的待机状态的相关事件
//			animator.GetBehaviour<FSMEvents_RHand_Idle>().OnEnterEvent += () => StartCoroutine(StateExitLayer(PlayerFSMLayer.AttackLayer));
//			animator.GetBehaviour<FSMEvents_RHand_Idle>().OnExitEvent += () => StartCoroutine(StateEnterLayer(PlayerFSMLayer.AttackLayer));
//			animator.GetBehaviour<FSMEvents_RHand_Slash>().OnExitEvent += () =>InitChangeLayer(PlayerFSMLayer.AttackLayer, 0f);
//			animator.GetBehaviour<FSMEvents_RHand_Idle>().OnExitEvent += () =>InitChangeLayer(PlayerFSMLayer.AttackLayer, 1f);
//			animator.GetBehaviour<FSMEvents_RHand_Idle>().OnUpdateEvent += () => ChangeLayer(PlayerFSMLayer.AttackLayer);


			//单手攻击状态的相关事件
//			animator.GetBehaviour<FSMEvents_RHand_Slash>().OnEnterEvent += () => StartCoroutine(StateEnterLayer(PlayerFSMLayer.AttackLayer));
			animator.GetBehaviour<FSMEvents_RHand_Slash>().OnUpdateEvent += () => { StateSetSpeed(0f, EPlayer_FSMCurve.spd_Atk_1H_Slash1_Z); };


//			animator.GetBehaviour<FSMEvents_LHand_ShieldUp>().OnExitEvent += () => InitChangeLayer(PlayerFSMLayer.DefenseLayer, 0f);
//			animator.GetBehaviour<FSMEvents_LHand_Idle>().OnExitEvent += () => InitChangeLayer(PlayerFSMLayer.DefenseLayer, 1f);
//			animator.GetBehaviour<FSMEvents_LHand_Idle>().OnUpdateEvent += () => ChangeLayer(PlayerFSMLayer.DefenseLayer);


			//攻击层级的待机状态的相关事件
			animator.GetBehaviour<FSMEvents_LHand_Idle>().OnEnterEvent += () => StartCoroutine(StateExitLayer(EPlayer_FSMLayer.DefenseLayer));
			animator.GetBehaviour<FSMEvents_LHand_Idle>().OnExitEvent += () => StartCoroutine(StateEnterLayer(EPlayer_FSMLayer.DefenseLayer));

			//单手防御状态的相关事件
//			animator.GetBehaviour<FSMEvents_LHand_ShieldUp>().OnEnterEvent += () => StartCoroutine(StateEnterLayer(PlayerFSMLayer.DefenseLayer));
		}

		/// <summary>使输入失效，锁定攻击，且锁定平面移动</summary>
		private void StateLock() {
//			pi.EnableInput = false;
			lockMove = true;
			lockAction = true;
		}

		/// <summary>重置冲量速度，使输入生效，解锁攻击，且解锁平面移动</summary>
		private void StateUnlock() {
//			pi.EnableInput = true;
			lockMove = false;
			lockAction = false;
		}

		/// <summary>设置对应动画状态的Y轴曲线/固定速度，以及Z轴曲线/固定速度</summary>
		/// <param name="speed_y">Y轴固定速度</param>
		/// <param name="speed_z">Z轴固定速度</param>
		private void StateSetSpeed(float speed_y, float speed_z) {
			thrustVector = model.transform.up * speed_y + model.transform.forward * speed_z;
		}

		/// <summary>设置对应动画状态的Y轴曲线/固定速度，以及Z轴曲线/固定速度</summary>
		/// <param name="speed_y">Y轴固定速度</param>
		/// <param name="speed_z">Z轴曲线速度</param>
		private void StateSetSpeed(float speed_y, EPlayer_FSMCurve speed_z) {
			thrustVector = model.transform.up * speed_y + model.transform.forward * animator.GetFloat(speed_z.TS());
		}

		/// <summary>设置对应动画状态的Y轴曲线/固定速度，以及Z轴曲线/固定速度</summary>
		/// <param name="speed_y">Y轴曲线速度</param>
		/// <param name="speed_z">Z轴固定速度</param>
		private void StateSetSpeed(EPlayer_FSMCurve speed_y, float speed_z) {
			thrustVector = model.transform.up * animator.GetFloat(speed_y.TS()) + model.transform.forward * speed_z;
		}

		/// <summary>设置对应动画状态的Y轴曲线/固定速度，以及Z轴曲线/固定速度</summary>
		/// <param name="speed_y">Y轴曲线速度</param>
		/// <param name="speed_z">Z轴曲线速度</param>
		private void StateSetSpeed(EPlayer_FSMCurve speed_y, EPlayer_FSMCurve speed_z) {
			thrustVector = model.transform.up * animator.GetFloat(speed_y.TS()) + model.transform.forward * animator.GetFloat(speed_z.TS());
		}


		private IEnumerator StateEnterLayer(EPlayer_FSMLayer layer, float lerpSpeed = 0.5f, float target = 1.0f) {
			animator.SetLayerWeight(0, 0);
			float curWeight = animator.GetLayerWeight(animator.GetLayerIndex(layer.TS()));
			layerWeightTarget = Mathf.Clamp01(target);
			while(curWeight < layerWeightTarget) {
				curWeight = Mathf.Lerp(curWeight, layerWeightTarget, lerpSpeed);
				animator.SetLayerWeight(animator.GetLayerIndex(layer.TS()), curWeight);
				yield return new WaitForSeconds(0.02f);
			}
		}

		private IEnumerator StateExitLayer(EPlayer_FSMLayer layer, float lerpSpeed = 0.5f, float target = 0f) {
			animator.SetLayerWeight(0, 1);
			float curWeight = animator.GetLayerWeight(animator.GetLayerIndex(layer.TS()));
			layerWeightTarget = Mathf.Clamp01(target);
			while(curWeight > layerWeightTarget) {
				curWeight = Mathf.Lerp(curWeight, layerWeightTarget, lerpSpeed);
				animator.SetLayerWeight(animator.GetLayerIndex(layer.TS()), curWeight);
				yield return new WaitForSeconds(0.02f);
			}
		}

//		public void InitChangeLayer(PlayerFSMLayer layer,float target) {
//			layerWeightTarget = Mathf.Clamp01(target);
//		}
//	
//
//		public void ChangeLayer(PlayerFSMLayer layer,float lerpSpeed = 1f) {
//			var weight = animator.GetLayerWeight(animator.GetLayerIndex(layer.TS()));
//			weight = Mathf.Lerp(weight, layerWeightTarget, lerpSpeed);
//			animator.SetLayerWeight(animator.GetLayerIndex(layer.TS()), weight);
//		}


//		/// <summary>切换FSM层级（使用Lerp平滑切换）</summary>
//		/// <remarks>思路：将指定层级的权重平滑增加到1.0/0</remarks>
//		/// <param name="layer">改变权重的层级</param>
//		/// <param name="lerpSpeed">插值时间</param>
//		private void StateChangeLayer(PlayerFSMLayer layer, float lerpSpeed = 0.4f) {
//			//使用插值，平缓切换FSM层级
//			float curWeight = animator.GetLayerWeight(animator.GetLayerIndex(layer.TS()));
//			curWeight = Mathf.Lerp(curWeight, lerpTarget, lerpSpeed);
//			animator.SetLayerWeight(animator.GetLayerIndex(layer.TS()), curWeight);
//		}

		#endregion


		#region ［其他方法］

//		/// <summary>检查当前动画状态</summary>
//		/// <param name="stateName">指定的状态名称</param>
//		/// <param name="layerName">指定的层级名称</param>
//		/// <returns></returns>
//		private bool CheckState(PlayerFSMState stateName, PlayerFSMLayer layerName = PlayerFSMLayer.BaseLayer) {
//			int layerIndex = animator.GetLayerIndex(layerName.TS());
//			bool result = animator.GetCurrentAnimatorStateInfo(layerIndex).IsName(stateName.TS());
//			return result;
//		}

		/// <summary>更新Root Motion</summary>
		/// <remarks>仅在第三段攻击时应用</remarks>
		/// <param name="deltaPosObj"></param>
		private void OnRMUpdate(object deltaPosObj) {
			if(animator.CheckState(EPlayer_FSMState.RHand_Slash3))
				deltaVector += 0.5f * deltaVector + 0.5f * (Vector3) deltaPosObj;
		}

		#endregion
	}
}