using System;
using System.Reflection;
using DiveOrDieCoop.Networking.Protocol;
using DRS.Patterns;
using ED.Diving;
using HarmonyLib;
using UnityEngine;

namespace DiveOrDieCoop.Diving
{
	public sealed class RemoteDiverController : MonoBehaviour, IDamageable
	{
		public int PeerId => this._peerId;
		public bool IsAlive { get; private set; } = true;

		private const float MaxExtrapolationSeconds = 0.5f;

		private DivingWaypoint _waypoint;
		private int _peerId;
		private string _playerName = string.Empty;

		private Entity _entity;
		private Rigidbody2D _rigidbody;
		private bool _hasSnapshot;

		private Vector3 _interpFromPos;
		private Vector3 _interpToPos;
		private Quaternion _interpFromRot;
		private Quaternion _interpToRot;
		private float _interpElapsed;
		private Vector2 _lastVelocity;

		private bool _deathStarted;

		private Component _skeleton;
		private object _animationState;
		private MethodInfo _setAnimationMethod;
		private MethodInfo _setEmptyAnimationMethod;

		private string _currentAnim0;
		private string _currentAnim1;

		public void Initialize(int peerId = 0)
		{
			this._peerId = peerId;
			this._entity = base.GetComponentInChildren<Entity>(true);
			this._rigidbody = base.GetComponent<Rigidbody2D>();
			this._hasSnapshot = false;
			this._deathStarted = false;
			this.IsAlive = true;

			Type skeletonType = Type.GetType("Spine.Unity.SkeletonAnimation, spine-unity") ?? Type.GetType("Spine.Unity.SkeletonAnimation, Assembly-CSharp");

			if (skeletonType != null)
			{
				this._skeleton = base.GetComponentInChildren(skeletonType, true);

				if (this._skeleton != null)
				{
					PropertyInfo stateProp = skeletonType.GetProperty("AnimationState");

					if (stateProp != null)
					{
						this._animationState = stateProp.GetValue(this._skeleton);

						if (this._animationState != null)
						{
							this._setAnimationMethod = this._animationState.GetType().GetMethod("SetAnimation", new Type[] { typeof(int), typeof(string), typeof(bool) });
							this._setEmptyAnimationMethod = this._animationState.GetType().GetMethod("SetEmptyAnimation", new Type[] { typeof(int), typeof(float) });
						}
					}
				}
			}
		}

		public void ReceiveDamage(int value, EntityAttributeID damagedAttribute, IDamageSource source) { }
		public void ReceiveDamage(int value, EntityAttributeID damagedAttribute, IDamageSource source, bool ignoreResistance) { }

		public void Die(IDamageSource source)
		{
			this.StartDeath("dead", this._lastVelocity);
		}

		public void SetSkin(string skinId)
		{
			if (string.IsNullOrEmpty(skinId) || this._skeleton == null)
				return;

			try
			{
				PropertyInfo skeletonProp = this._skeleton.GetType().GetProperty("Skeleton");
				object skel = skeletonProp?.GetValue(this._skeleton);

				if (skel != null)
				{
					MethodInfo setSkin = skel.GetType().GetMethod("SetSkin", new Type[] { typeof(string) });
					setSkin?.Invoke(skel, new object[] { skinId });
				}
			}
			catch { }
		}

		public void ApplySnapshot(DiverSnapshotMessage snapshot)
		{
			Vector2 targetPosition = new Vector2(snapshot.PosX, snapshot.PosY);
			Quaternion targetRotation = Quaternion.Euler(0f, 0f, snapshot.RotZ);
			bool wasAlive = this.IsAlive;

			this.IsAlive = snapshot.IsDead ? false : snapshot.Health > 0;

			if (!this._hasSnapshot)
			{
				base.transform.position = targetPosition;
				base.transform.rotation = targetRotation;
				this._hasSnapshot = true;
			}

			this._lastVelocity = new Vector2(snapshot.VelX, snapshot.VelY);

			if (!this._deathStarted)
			{
				this._interpFromPos = base.transform.position;
				this._interpToPos = targetPosition;
				this._interpFromRot = base.transform.rotation;
				this._interpToRot = targetRotation;
				this._interpElapsed = 0f;
			}

			if (wasAlive && !this.IsAlive)
			{
				base.transform.position = targetPosition;
				base.transform.rotation = targetRotation;
				this.StartDeath(
					string.IsNullOrEmpty(snapshot.DeathAnimation) ? "dead" : snapshot.DeathAnimation,
					this._lastVelocity
				);
			}

			if (this._entity != null)
			{
				this._entity.SetAttributeValue(EntityAttributeID.HEALTH, snapshot.Health);
				this._entity.SetAttributeValue(EntityAttributeID.OXYGEN, snapshot.Oxygen);
			}

			Vector3 scale = base.transform.localScale;
			scale.y = snapshot.ScaleY == 0f ? scale.y : snapshot.ScaleY;
			base.transform.localScale = scale;

			if (!this._deathStarted && this._animationState != null && this._setAnimationMethod != null)
			{
				if (snapshot.Anim0 != this._currentAnim0 && !string.IsNullOrEmpty(snapshot.Anim0))
				{
					this._setAnimationMethod.Invoke(this._animationState, new object[] { 0, snapshot.Anim0, true });
					this._currentAnim0 = snapshot.Anim0;
				}

				if (snapshot.Anim1 != this._currentAnim1)
				{
					if (string.IsNullOrEmpty(snapshot.Anim1))
					{
						if (this._setEmptyAnimationMethod != null)
							this._setEmptyAnimationMethod.Invoke(this._animationState, new object[] { 1, 0.2f });
					}
					else
					{
						this._setAnimationMethod.Invoke(this._animationState, new object[] { 1, snapshot.Anim1, true });
					}

					this._currentAnim1 = snapshot.Anim1;
				}
			}

			this.UpdateWaypoint(snapshot);
		}

		private void StartDeath(string animationName, Vector2 initialVelocity)
		{
			if (this._deathStarted)
				return;

			this._deathStarted = true;
			this.IsAlive = false;
			this._currentAnim0 = animationName;
			this._currentAnim1 = string.Empty;

			if (this._rigidbody != null)
			{
				this._rigidbody.position = base.transform.position;
				this._rigidbody.rotation = base.transform.eulerAngles.z;
				this._rigidbody.bodyType = RigidbodyType2D.Dynamic;
				this._rigidbody.simulated = true;
				this._rigidbody.gravityScale = 0.1f;
				this._rigidbody.velocity = initialVelocity;
			}

			foreach (Collider2D collider in base.GetComponentsInChildren<Collider2D>(true))
			{
				if (!collider.isTrigger)
					collider.enabled = true;
			}

			if (this._waypoint != null)
				this._waypoint.Toggle(false);

			if (this._animationState != null && this._setAnimationMethod != null)
			{
				try
				{
					this._setAnimationMethod.Invoke(this._animationState, new object[]
					{
						0,
						string.IsNullOrEmpty(animationName) ? "dead" : animationName,
						false
					});
				}
				catch { }
			}

			if (this._animationState != null && this._setEmptyAnimationMethod != null)
			{
				try
				{
					this._setEmptyAnimationMethod.Invoke(this._animationState, new object[] { 1, 0.1f });
				}
				catch { }
			}
		}

		private void UpdateWaypoint(DiverSnapshotMessage snapshot)
		{
			if (string.IsNullOrEmpty(this._playerName) && !string.IsNullOrEmpty(snapshot.PlayerName))
				this._playerName = snapshot.PlayerName;

			if (this._waypoint == null && SingletonObjectPool<DivingWaypointManager, DivingWaypoint>.HasInstance())
			{
				string label = !string.IsNullOrEmpty(this._playerName) ? this._playerName : $"Player {this._peerId}";
				Sprite icon = GetWaypointSprite();

				if (icon != null)
					this._waypoint = SingletonObjectPool<DivingWaypointManager, DivingWaypoint>.Instance.DisplayWaypoint(icon, label, this.transform);
			}

			if (this._waypoint != null)
				this._waypoint.Toggle(this.IsAlive);
		}

		private static Sprite GetWaypointSprite()
		{
			DeadDiverManager mgr = GameObject.FindObjectOfType<DeadDiverManager>();

			if (mgr != null)
			{
				Sprite s = AccessTools.Field(typeof(DeadDiverManager), "_deadDiverWaypointSprite")?.GetValue(mgr) as Sprite;
				if (s != null)
					return s;
			}

			return Resources.Load<Sprite>("Sprites/UI/Waypoint_Player") ?? Resources.Load<Sprite>("Sprites/UI/Waypoint_Default");
		}

		private void Update()
		{
			if (!this._hasSnapshot || this._deathStarted)
				return;

			this._interpElapsed += Time.deltaTime;
			float interval = DiveSyncManager.SendIntervalSeconds;

			if (interval <= 0f)
			{
				base.transform.position = this._interpToPos;
			}
			else if (this._interpElapsed <= interval)
			{
				float t = Mathf.Clamp01(this._interpElapsed / interval);
				base.transform.position = Vector3.Lerp(this._interpFromPos, this._interpToPos, t);
			}
			else
			{
				float extrapolatedSeconds = Mathf.Min(this._interpElapsed - interval, MaxExtrapolationSeconds);
				base.transform.position = this._interpToPos + (Vector3)(this._lastVelocity * extrapolatedSeconds);
			}

			float rotT = interval <= 0f ? 1f : Mathf.Clamp01(this._interpElapsed / interval);
			base.transform.rotation = Quaternion.Lerp(this._interpFromRot, this._interpToRot, rotT);
		}

		private void OnDisable()
		{
			if (this._waypoint != null)
				this._waypoint.Toggle(false);
		}

		private void OnDestroy()
		{
			if (this._waypoint != null)
			{
				this._waypoint.Toggle(false);
				this._waypoint = null;
			}
		}
	}
}