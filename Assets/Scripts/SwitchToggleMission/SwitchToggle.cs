using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

namespace SwitchToggleMission
{
    public class SwitchToggle : MonoBehaviour, IPointerClickHandler
    {
        public RectTransform knob;
        public Image backgroundImage;
        public Color offColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        public Color onColor = Color.green;
        public float knobOffX = -25f;
        public float knobOnX = 25f;
        public float slideDuration = 0.2f;

        public bool IsOn { get; private set; }

        private Coroutine currentAnimation;

        private void Start()
        {
            SetVisual(false, true);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Toggle();
            if (SwitchMinigameUI.Instance != null)
            {
                SwitchMinigameUI.Instance.UpdateStatus();
            }
        }

        public void Toggle()
        {
            IsOn = !IsOn;
            SetVisual(IsOn);
        }

        public void SetVisual(bool on, bool instant = false)
        {
            if (knob == null || backgroundImage == null)
            {
                return;
            }

            if (currentAnimation != null)
            {
                StopCoroutine(currentAnimation);
                currentAnimation = null;
            }

            float targetX = on ? knobOnX : knobOffX;
            Color targetColor = on ? onColor : offColor;

            if (instant)
            {
                Vector2 anchored = knob.anchoredPosition;
                anchored.x = targetX;
                knob.anchoredPosition = anchored;
                backgroundImage.color = targetColor;
                return;
            }

            currentAnimation = StartCoroutine(Animate(targetX, targetColor));
        }

        public void ResetSwitch()
        {
            IsOn = false;
            SetVisual(false, true);
        }

        private IEnumerator Animate(float targetX, Color targetColor)
        {
            Vector2 startPosition = knob.anchoredPosition;
            Color startColor = backgroundImage.color;
            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, slideDuration);

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float smoothT = Mathf.SmoothStep(0f, 1f, t);

                Vector2 nextPosition = startPosition;
                nextPosition.x = Mathf.Lerp(startPosition.x, targetX, smoothT);
                knob.anchoredPosition = nextPosition;
                backgroundImage.color = Color.Lerp(startColor, targetColor, smoothT);
                yield return null;
            }

            Vector2 finalPosition = knob.anchoredPosition;
            finalPosition.x = targetX;
            knob.anchoredPosition = finalPosition;
            backgroundImage.color = targetColor;
            currentAnimation = null;
        }
    }
}
