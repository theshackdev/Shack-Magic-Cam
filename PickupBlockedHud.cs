using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheShack.MagicCam;

internal static class PickupBlockedHud
{
	private const float VisibilityCheckInterval = 0.2f;
	private const float PanelWidth = 460f;
	private const float HorizontalPadding = 18f;
	private const float TopPadding = 12f;
	private const float BottomPadding = 14f;
	private const float TextSpacing = 4f;

	private static GameObject _panel = null!;
	private static RectTransform _panelRect = null!;
	private static TextMeshProUGUI _titleText = null!;
	private static TextMeshProUGUI _bodyText = null!;
	private static float _nextVisibilityCheck;
	private static float _lastHudX = float.NaN;
	private static float _lastHudY = float.NaN;
	private static int _lastComfortLevel = -1;
	private static int _lastFontSize = -1;

	internal static void Update()
	{
		if (Time.time < _nextVisibilityCheck) return;
		_nextVisibilityCheck = Time.time + VisibilityCheckInterval;

		if (Hud.IsUserHidden() || !Utils.ShouldShowPickupWarning())
		{
			Hide();
			return;
		}

		if (!_panel)
		{
			_panelRect = null!;
			_titleText = null!;
			_bodyText = null!;
			if (!CreatePanel()) return;
		}

		GameObject panel = _panel!;
		if (!panel) return;
		bool wasHidden = !panel.activeSelf;
		if (wasHidden || NeedsRefresh()) RefreshPanel();
		if (wasHidden) panel.SetActive(true);
	}

	internal static void LanguageChanged()
	{
		_lastComfortLevel = -1;
	}

	internal static void Cleanup()
	{
		if (_panel) Object.Destroy(_panel);
		_panel = null!;
		_panelRect = null!;
		_titleText = null!;
		_bodyText = null!;
		_nextVisibilityCheck = 0f;
		_lastHudX = float.NaN;
		_lastHudY = float.NaN;
		_lastComfortLevel = -1;
		_lastFontSize = -1;
	}

	private static bool CreatePanel()
	{
		MessageHud messageHud = MessageHud.instance;
		Hud hud = Hud.instance;
		if (!messageHud || !hud || !messageHud.m_messageText || !messageHud.m_messageText.font) return false;

		_panel = new GameObject("ShackMagicCam_PickupBlocked", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
		_panel.SetActive(false);
		_panel.transform.SetParent(messageHud.transform, false);
		_panel.transform.SetAsLastSibling();
		_panelRect = (RectTransform)_panel.transform;

		Image background = _panel.GetComponent<Image>();
		background.raycastTarget = false;
		ApplyPanelStyle(background, hud);

		_titleText = CreateText("Title", messageHud.m_messageText);
		_bodyText = CreateText("Body", messageHud.m_messageText);
		_titleText.fontStyle = FontStyles.Bold;
		_titleText.alignment = TextAlignmentOptions.TopLeft;
		_bodyText.alignment = TextAlignmentOptions.TopLeft;
		_bodyText.textWrappingMode = TextWrappingModes.Normal;
		return true;
	}

	private static TextMeshProUGUI CreateText(string name, TMP_Text fontSource)
	{
		GameObject textObject = new(name, typeof(RectTransform), typeof(CanvasRenderer));
		textObject.transform.SetParent(_panel.transform, false);
		TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
		text.font = fontSource.font;
		text.fontSharedMaterial = fontSource.fontSharedMaterial;
		text.color = fontSource.color;
		text.raycastTarget = false;
		text.enableAutoSizing = false;
		text.overflowMode = TextOverflowModes.Overflow;
		return text;
	}

	private static void ApplyPanelStyle(Image background, Hud hud)
	{
		Image source = null!;
		if (hud.m_pieceSelectionWindow)
		{
			Transform panelBackground = hud.m_pieceSelectionWindow.transform.Find("Bkg2");
			if (panelBackground) source = panelBackground.GetComponent<Image>();
		}

		if (source)
		{
			background.sprite = source.sprite;
			background.type = source.type;
			background.color = source.color;
			background.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
			return;
		}

		// Measured on Valheim 1.0.16: Hud.m_pieceSelectionWindow is still present (inactive, but
		// Transform.Find works on inactive objects) and its "Bkg2" child is still there, so the
		// lookup above succeeds and this fallback does not run. The build window itself moved to
		// Hud.m_buildUi ("BuildUIV2"), whose four children carry no panel background to borrow -
		// so if a future update does drop Bkg2, there is nothing there to retarget to and this
		// flat colour is the answer. Warn rather than fail silently.
		Plugin.Log.LogWarning(
			"Could not find the vanilla build-window background (Hud.m_pieceSelectionWindow/Bkg2) - using a flat colour.");
		background.color = new Color(0.06f, 0.055f, 0.045f, 0.92f);
	}

	private static bool NeedsRefresh()
	{
		if (_lastComfortLevel != Plugin.minimumComfortLevel.Value) return true;
		if (_lastFontSize != Plugin.pickupHudFontSize.Value) return true;
		if (!Mathf.Approximately(_lastHudX, Plugin.pickupHudX.Value)) return true;
		return !Mathf.Approximately(_lastHudY, Plugin.pickupHudY.Value);
	}

	private static void RefreshPanel()
	{
		_lastComfortLevel = Plugin.minimumComfortLevel.Value;
		_lastFontSize = Plugin.pickupHudFontSize.Value;
		_lastHudX = Plugin.pickupHudX.Value;
		_lastHudY = Plugin.pickupHudY.Value;

		_titleText.text = Localization.instance.Localize("$shackmagiccam_pickup_blocked_title");
		_bodyText.text = Localization.instance.Localize("$shackmagiccam_pickup_blocked", _lastComfortLevel.ToString());
		_titleText.fontSize = Mathf.Max(18, _lastFontSize + 2);
		_bodyText.fontSize = _lastFontSize;

		float titleHeight = _titleText.fontSize + 6f;
		float bodyHeight = Mathf.Max(_bodyText.fontSize * 2.4f, _bodyText.GetPreferredValues(_bodyText.text, PanelWidth - HorizontalPadding * 2f, 0f).y);
		float panelHeight = TopPadding + titleHeight + TextSpacing + bodyHeight + BottomPadding;

		Vector2 anchor = new(_lastHudX, _lastHudY);
		_panelRect.anchorMin = anchor;
		_panelRect.anchorMax = anchor;
		_panelRect.pivot = anchor;
		_panelRect.anchoredPosition = Vector2.zero;
		_panelRect.sizeDelta = new Vector2(PanelWidth, panelHeight);
		_panelRect.localScale = Vector3.one;

		RectTransform titleRect = _titleText.rectTransform;
		titleRect.anchorMin = new Vector2(0f, 1f);
		titleRect.anchorMax = new Vector2(1f, 1f);
		titleRect.pivot = new Vector2(0.5f, 1f);
		titleRect.anchoredPosition = new Vector2(0f, -TopPadding);
		titleRect.sizeDelta = new Vector2(-HorizontalPadding * 2f, titleHeight);

		RectTransform bodyRect = _bodyText.rectTransform;
		bodyRect.anchorMin = new Vector2(0f, 1f);
		bodyRect.anchorMax = new Vector2(1f, 1f);
		bodyRect.pivot = new Vector2(0.5f, 1f);
		bodyRect.anchoredPosition = new Vector2(0f, -TopPadding - titleHeight - TextSpacing);
		bodyRect.sizeDelta = new Vector2(-HorizontalPadding * 2f, bodyHeight);
	}

	private static void Hide()
	{
		if (_panel && _panel.activeSelf) _panel.SetActive(false);
	}
}
