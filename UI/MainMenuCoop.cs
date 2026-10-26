using System;
using System.Collections.Generic;
using DiveOrDieCoop.Networking;
using DiveOrDieCoop.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DiveOrDieCoop.Patches
{
	public enum HostModeType
	{
		None,
		Steam,
		Lan
	}

	public enum CoopMenuState
	{
		Root,       // [ CO-OP ]
		HostOrJoin, // [ HOST ] [ JOIN ]
		SteamOrLan  // [ STEAM ] [ LAN ]
	}

	[HarmonyPatch(typeof(MainMenu))]
	public static class MainMenuCoopPatch
	{
		public static bool IsHostCoopMode { get; private set; }
		public static HostModeType CurrentHostMode { get; private set; } = HostModeType.None;

		private static Button _btn1;
		private static Button _btn2;
		private static CoopMenuState _currentState = CoopMenuState.Root;

		private static Vector2 _fullSize;
		private static Vector2 _halfSize;
		private static Vector2 _centerPos;
		private static Vector2 _leftPos;
		private static Vector2 _rightPos;

		// One-frame suppression so the outside-click watcher never re-evaluates
		// the exact same click that a coop button's own onClick just handled.
		private static bool _suppressClickCheck;
		private static readonly List<RaycastResult> _raycastResultsBuffer = new List<RaycastResult>();

		// True from the moment OpenSlotGroup() is called for a coop host/join flow
		// until a slot is chosen (or the player backs out). While true the outside-click
		// watcher must not touch IsHostCoopMode/CurrentHostMode at all — clicking a save
		// slot button is not a click on _btn1/_btn2 and would otherwise be misread as
		// "clicked outside the coop menu", silently cancelling the host/join intent
		// before ChooseSlot_Prefix ever reads it.
		private static bool _slotSelectionActive;

		[HarmonyPatch("Start")]
		[HarmonyPostfix]
		private static void Start_Postfix(MainMenu __instance)
		{
			try
			{
				IsHostCoopMode = false;
				CurrentHostMode = HostModeType.None;
				_currentState = CoopMenuState.Root;
				_suppressClickCheck = false;
				_slotSelectionActive = false;

				Button continueBtn = AccessTools.Field(typeof(MainMenu), "_continueButton")?.GetValue(__instance) as Button;
				Button newGameBtn = AccessTools.Field(typeof(MainMenu), "_newGameButton")?.GetValue(__instance) as Button;
				List<Button> mainButtons = AccessTools.Field(typeof(MainMenu), "_mainButtons")?.GetValue(__instance) as List<Button>;

				Button templateBtn = (newGameBtn != null && newGameBtn.gameObject.activeInHierarchy) ? newGameBtn : continueBtn;
				if (templateBtn == null) return;

				Transform parent = templateBtn.transform.parent;

				// Calculate exact dimensions and positioning
				RectTransform contRt = continueBtn != null ? continueBtn.GetComponent<RectTransform>() : null;
				RectTransform newRt = newGameBtn != null ? newGameBtn.GetComponent<RectTransform>() : null;
				RectTransform templRt = templateBtn.GetComponent<RectTransform>();

				float posX = templRt.anchoredPosition.x;
				float topY = templRt.anchoredPosition.y;
				float stepY = templRt.rect.height > 0 ? templRt.rect.height + 12f : 55f;

				if (contRt != null && newRt != null)
				{
					float diff = Mathf.Abs(contRt.anchoredPosition.y - newRt.anchoredPosition.y);
					if (diff > 10f) stepY = diff;
					topY = Mathf.Max(contRt.anchoredPosition.y, newRt.anchoredPosition.y);
				}

				float fullWidth = templRt.rect.width > 0 ? templRt.rect.width : 280f;
				float btnHeight = templRt.rect.height > 0 ? templRt.rect.height : 45f;
				float spacing = 8f;
				float halfWidth = (fullWidth - spacing) * 0.5f;

				// Sizing configurations
				_fullSize = new Vector2(fullWidth, btnHeight);
				_halfSize = new Vector2(halfWidth, btnHeight);

				// Horizontal positions on the exact same row (directly above Continue)
				_centerPos = new Vector2(posX, topY + stepY);
				_leftPos = new Vector2(posX - (halfWidth * 0.5f + spacing * 0.5f), topY + stepY);
				_rightPos = new Vector2(posX + (halfWidth * 0.5f + spacing * 0.5f), topY + stepY);

				// Reset coop state if native singleplayer buttons are clicked
				if (continueBtn != null) continueBtn.onClick.AddListener(ResetToDefaultMenu);
				if (newGameBtn != null) newGameBtn.onClick.AddListener(ResetToDefaultMenu);

				// -------------------------------------------------------------
				// Clone native buttons
				// -------------------------------------------------------------
				if (_btn1 != null) UnityEngine.Object.Destroy(_btn1.gameObject);
				if (_btn2 != null) UnityEngine.Object.Destroy(_btn2.gameObject);

				_btn1 = CloneMenuButton(templateBtn, parent, "CoopMenuBtn1");
				_btn2 = CloneMenuButton(templateBtn, parent, "CoopMenuBtn2");

				CopyRectTransform(templRt, _btn1.GetComponent<RectTransform>());
				CopyRectTransform(templRt, _btn2.GetComponent<RectTransform>());

				// -------------------------------------------------------------
				// Button Actions
				// -------------------------------------------------------------

				_btn1.onClick = new Button.ButtonClickedEvent();
				_btn1.onClick.AddListener(() =>
				{
					_suppressClickCheck = true;

					switch (_currentState)
					{
						case CoopMenuState.Root: // Clicked [ CO-OP ] -> Split into [ HOST ] [ JOIN ]
							SetMenuState(CoopMenuState.HostOrJoin);
							SelectButton(_btn1);
							break;

						case CoopMenuState.HostOrJoin: // Clicked [ HOST ] -> Split into [ STEAM ] [ LAN ]
							SetMenuState(CoopMenuState.SteamOrLan);
							SelectButton(_btn1);
							break;

						case CoopMenuState.SteamOrLan: // Clicked [ STEAM ] -> Select Slot & Host Steam
							IsHostCoopMode = true;
							CurrentHostMode = HostModeType.Steam;
							_slotSelectionActive = true;
							__instance.SetSlotMode(true);
							__instance.OpenSlotGroup();
							break;
					}
				});

				_btn2.onClick = new Button.ButtonClickedEvent();
				_btn2.onClick.AddListener(() =>
				{
					_suppressClickCheck = true;

					switch (_currentState)
					{
						case CoopMenuState.HostOrJoin: // Clicked [ JOIN ] -> Open lobby browser overlay
							IsHostCoopMode = false;
							CurrentHostMode = HostModeType.None;
							ResetToDefaultMenu();
							CoopDebugOverlay.Instance?.OpenToSteamTab();
							break;

						case CoopMenuState.SteamOrLan: // Clicked [ LAN ] -> Select Slot & Host LAN
							IsHostCoopMode = true;
							CurrentHostMode = HostModeType.Lan;
							_slotSelectionActive = true;
							__instance.SetSlotMode(true);
							__instance.OpenSlotGroup();
							break;
					}
				});

				if (mainButtons != null)
				{
					mainButtons.Add(_btn1);
					mainButtons.Add(_btn2);
				}

				// Attach Click-Outside Watcher to automatically collapse on click outside
				if (__instance.gameObject.GetComponent<MainMenuClickOutsideWatcher>() == null)
				{
					__instance.gameObject.AddComponent<MainMenuClickOutsideWatcher>();
				}

				SetMenuState(CoopMenuState.Root);
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[MainMenuCoopPatch] Error creating CO-OP buttons: {ex.Message}");
			}
		}

		[HarmonyPatch("ChooseSlot")]
		[HarmonyPrefix]
		private static void ChooseSlot_Prefix(MainMenu __instance, int slotNumber)
		{
			// A slot was actually chosen — the watcher-suspension window is over
			// regardless of which branch below fires.
			_slotSelectionActive = false;

			if (IsHostCoopMode && NetworkManager.Instance != null)
			{
				if (CurrentHostMode == HostModeType.Steam)
				{
#if STEAMWORKS_NET
					NetworkManager.Instance.HostSteam();
#else
					NetworkManager.Instance.HostLan();
#endif
				}
				else if (CurrentHostMode == HostModeType.Lan)
				{
					NetworkManager.Instance.HostLan();
				}
			}
		}

		// If the player opened the slot picker for a coop host/join and then backs out
		// via Cancel (MainMenu.CloseSlotGroup) WITHOUT choosing a slot, ChooseSlot_Prefix
		// never runs, so _slotSelectionActive/IsHostCoopMode would otherwise stay stuck.
		// Reset fully in that case so the coop button row goes back to [ CO-OP ].
		[HarmonyPatch("CloseSlotGroup")]
		[HarmonyPostfix]
		private static void CloseSlotGroup_Postfix()
		{
			if (_slotSelectionActive)
			{
				_slotSelectionActive = false;
				ResetToDefaultMenu();
			}
		}

		private static Button CloneMenuButton(Button template, Transform parent, string name)
		{
			Button btn = UnityEngine.Object.Instantiate(template, parent);
			btn.name = name;
			btn.gameObject.SetActive(true);

			// Remove localization scripts so game doesn't overwrite custom labels
			foreach (var mb in btn.GetComponentsInChildren<MonoBehaviour>(true))
			{
				if (mb != null && mb.GetType().Name.IndexOf("Loc", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					UnityEngine.Object.DestroyImmediate(mb);
				}
			}

			return btn;
		}

		private static void CopyRectTransform(RectTransform src, RectTransform dst)
		{
			if (src == null || dst == null) return;
			dst.anchorMin = src.anchorMin;
			dst.anchorMax = src.anchorMax;
			dst.pivot = src.pivot;
		}

		private static void SetMenuState(CoopMenuState state)
		{
			_currentState = state;

			switch (state)
			{
				case CoopMenuState.Root:
					if (_btn1 != null)
					{
						_btn1.gameObject.SetActive(true);
						RectTransform rt1 = _btn1.GetComponent<RectTransform>();
						rt1.sizeDelta = _fullSize;
						rt1.anchoredPosition = _centerPos;
						_btn1.transform.SetAsLastSibling();
						SetButtonText(_btn1, "CO-OP");
					}
					if (_btn2 != null)
					{
						_btn2.gameObject.SetActive(false);
					}
					break;

				case CoopMenuState.HostOrJoin:
					if (_btn1 != null)
					{
						_btn1.gameObject.SetActive(true);
						RectTransform rt1 = _btn1.GetComponent<RectTransform>();
						rt1.sizeDelta = _halfSize;
						rt1.anchoredPosition = _leftPos;
						_btn1.transform.SetAsLastSibling();
						SetButtonText(_btn1, "HOST");
					}
					if (_btn2 != null)
					{
						_btn2.gameObject.SetActive(true);
						RectTransform rt2 = _btn2.GetComponent<RectTransform>();
						rt2.sizeDelta = _halfSize;
						rt2.anchoredPosition = _rightPos;
						_btn2.transform.SetAsLastSibling();
						SetButtonText(_btn2, "JOIN");
					}
					break;

				case CoopMenuState.SteamOrLan:
					if (_btn1 != null)
					{
						_btn1.gameObject.SetActive(true);
						RectTransform rt1 = _btn1.GetComponent<RectTransform>();
						rt1.sizeDelta = _halfSize;
						rt1.anchoredPosition = _leftPos;
						_btn1.transform.SetAsLastSibling();
						SetButtonText(_btn1, "STEAM");
					}
					if (_btn2 != null)
					{
						_btn2.gameObject.SetActive(true);
						RectTransform rt2 = _btn2.GetComponent<RectTransform>();
						rt2.sizeDelta = _halfSize;
						rt2.anchoredPosition = _rightPos;
						_btn2.transform.SetAsLastSibling();
						SetButtonText(_btn2, "LAN");
					}
					break;
			}
		}

		private static void SetButtonText(Button btn, string text)
		{
			if (btn == null) return;
			TextMeshProUGUI tmp = btn.GetComponentInChildren<TextMeshProUGUI>(true);
			if (tmp != null)
			{
				tmp.text = text;
				tmp.gameObject.SetActive(true);
			}
		}

		public static void ResetToDefaultMenu()
		{
			IsHostCoopMode = false;
			CurrentHostMode = HostModeType.None;
			_slotSelectionActive = false;
			SetMenuState(CoopMenuState.Root);
		}

		private static void SelectButton(Button btn)
		{
			if (EventSystem.current != null && btn != null)
			{
				EventSystem.current.SetSelectedGameObject(btn.gameObject);
			}
		}

		// -------------------------------------------------------------
		// Outside-click detection (watcher-facing API)
		// -------------------------------------------------------------

		internal static bool ConsumeSuppressFlag()
		{
			if (_suppressClickCheck)
			{
				_suppressClickCheck = false;
				return true;
			}
			return false;
		}

		/// <summary>
		/// True while the save-slot picker is open for a coop host/join flow. The
		/// outside-click watcher must do nothing at all during this window — see
		/// the field comment on _slotSelectionActive for why.
		/// </summary>
		internal static bool IsWatcherSuspended() => _slotSelectionActive;

		/// <summary>
		/// Uses the actual EventSystem raycast (respects whatever camera/render mode
		/// the canvas uses) instead of manual RectTransformUtility math with a
		/// hardcoded null camera, which silently fails on non-Overlay canvases.
		/// </summary>
		internal static bool IsClickingCoopButtons(Vector2 screenPos)
		{
			if (_currentState == CoopMenuState.Root) return true;
			if (EventSystem.current == null) return true;

			PointerEventData pointerData = new PointerEventData(EventSystem.current)
			{
				position = screenPos
			};

			_raycastResultsBuffer.Clear();
			EventSystem.current.RaycastAll(pointerData, _raycastResultsBuffer);

			foreach (RaycastResult result in _raycastResultsBuffer)
			{
				Transform t = result.gameObject != null ? result.gameObject.transform : null;
				while (t != null)
				{
					if ((_btn1 != null && t == _btn1.transform) || (_btn2 != null && t == _btn2.transform))
						return true;
					t = t.parent;
				}
			}

			return false;
		}

		internal static bool IsCoopButtonSelected()
		{
			if (_currentState == CoopMenuState.Root) return true;
			if (EventSystem.current == null) return true;

			GameObject sel = EventSystem.current.currentSelectedGameObject;
			if (sel == null) return true;

			if (_btn1 != null && sel == _btn1.gameObject) return true;
			if (_btn2 != null && sel == _btn2.gameObject) return true;

			return false;
		}
	}

	/// <summary>
	/// Monitors mouse clicks and keyboard/gamepad navigation to reset the CO-OP buttons back to [CO-OP] if deselected.
	/// </summary>
	public sealed class MainMenuClickOutsideWatcher : MonoBehaviour
	{
		private void Update()
		{
			// Slot picker is open for a coop host/join — don't touch coop state at all
			// until ChooseSlot (or a cancel back out of it) resolves it.
			if (MainMenuCoopPatch.IsWatcherSuspended())
			{
				return;
			}

			// A coop button's own onClick already handled this exact click — don't
			// re-evaluate it against (possibly already-resized) button bounds.
			if (MainMenuCoopPatch.ConsumeSuppressFlag())
			{
				return;
			}

			// Reset if clicked with mouse outside the CO-OP buttons
			if (Input.GetMouseButtonDown(0))
			{
				if (!MainMenuCoopPatch.IsClickingCoopButtons(Input.mousePosition))
				{
					MainMenuCoopPatch.ResetToDefaultMenu();
				}
			}

			// Reset if navigated away with keyboard or gamepad
			if (!MainMenuCoopPatch.IsCoopButtonSelected())
			{
				MainMenuCoopPatch.ResetToDefaultMenu();
			}
		}
	}
}