using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace Scenes.main_UdonProgramSources
{
    public class TextSlider : UdonSharpBehaviour
    {
        public Slider slider; // 在面板里拖入你的 Slider 组件
        public TextMeshProUGUI text;

        public float step = 0.1f;
        public float displayMultiplier = 1.0f;

        [UnityEngine.Tooltip("0: 整数, 1: 一位小数, 2: 两位小数")] [Range(0, 2)]
        public int decimalPlaces = 1;

        public bool useSpecialValue = false;
        public float specialValue = 0f;

        public string specialValueText = "";

        public UdonSharpBehaviour callbackTarget;
        public string callbackEvent;

        public void SetValueAndRefresh(float val)
        {
            slider.SetValueWithoutNotify(val);
            _OnSliderChanged();
        }

        public void _OnSliderChanged()
        {
            float currentValue = slider.value * displayMultiplier;
            string displayText = "";

            if (useSpecialValue && Mathf.Abs(currentValue - specialValue) < 0.001f &&
                !string.IsNullOrEmpty(specialValueText))
            {
                displayText = specialValueText;
            }
            else
            {
                switch (decimalPlaces)
                {
                    case 0: displayText = currentValue.ToString("F0"); break;
                    case 1: displayText = currentValue.ToString("F1"); break;
                    case 2: displayText = currentValue.ToString("F2"); break;
                    default: displayText = currentValue.ToString("F1"); break;
                }
            }

            text.text = displayText;

            if (callbackTarget != null && !string.IsNullOrEmpty(callbackEvent))
            {
                callbackTarget.SendCustomEvent(callbackEvent);
            }
        }

        public void _OnButtonP()
        {
            slider.value += step;
        }

        public void _OnButtonN()
        {
            slider.value -= step;
        }
    }
}