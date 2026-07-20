using System;
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace Scenes.main_UdonProgramSources
{
    public class TextDuoSlider : UdonSharpBehaviour
    {
        public Slider sliderA; // 在面板里拖入你的 Slider 组件
        public TextMeshProUGUI textA;
        public Slider sliderB; // 在面板里拖入你的 Slider 组件
        public TextMeshProUGUI textB;
        public float step = 0.1f;

        [UnityEngine.Tooltip("是否允许 Slider A 的值超过 Slider B 的值")]
        public bool allowAExceedB = true;

        [Header("Slider A Format")]
        [UnityEngine.Tooltip("0: 整数, 1: 一位小数, 2: 两位小数")]
        [Range(0, 2)]
        public int decimalPlacesA = 1;
        public bool useSpecialValueA = false;
        public float specialValueA = 0f;
        public string specialValueTextA = "";

        [Header("Slider B Format")]
        [UnityEngine.Tooltip("0: 整数, 1: 一位小数, 2: 两位小数")]
        [Range(0, 2)]
        public int decimalPlacesB = 1;
        public bool useSpecialValueB = false;
        public float specialValueB = 0f;
        public string specialValueTextB = "";

        private string _localizedSpecialValueTextA = "";
        private string _localizedSpecialValueTextB = "";
        
        public UdonSharpBehaviour callbackTarget;
        public string callbackEvent;

        public void SetValuesAndRefresh(float valA, float valB)
        {
            sliderA.SetValueWithoutNotify(valA);
            sliderB.SetValueWithoutNotify(valB);
            _OnSliderAChanged();
            _OnSliderBChanged();
        }

        public void _OnSliderAChanged()
        {
            if (!allowAExceedB && sliderA.value > (sliderB.maxValue - sliderB.value))
            {
                sliderA.value = sliderB.maxValue - sliderB.value;
            }

            RefreshTextA();
            if (callbackTarget != null && !string.IsNullOrEmpty(callbackEvent))
            {
                callbackTarget.SendCustomEvent(callbackEvent);
            }
        }

        public void _OnSliderBChanged()
        {
            if (!allowAExceedB && (sliderB.maxValue - sliderB.value) < sliderA.value)
            {
                sliderB.value = sliderB.maxValue - sliderA.value;
            }

            RefreshTextB();
            if (callbackTarget != null && !string.IsNullOrEmpty(callbackEvent))
            {
                callbackTarget.SendCustomEvent(callbackEvent);
            }
        }

        public void SetSpecialValueTexts(string valueA, string valueB)
        {
            _localizedSpecialValueTextA = valueA;
            _localizedSpecialValueTextB = valueB;
            RefreshTextA();
            RefreshTextB();
        }

        private void RefreshTextA()
        {
            float currentValue = sliderA.value;
            string displayText = "";
            string activeSpecialValueTextA = string.IsNullOrEmpty(_localizedSpecialValueTextA)
                ? specialValueTextA
                : _localizedSpecialValueTextA;

            if (useSpecialValueA && Mathf.Abs(currentValue - specialValueA) < 0.001f && !string.IsNullOrEmpty(activeSpecialValueTextA))
            {
                displayText = activeSpecialValueTextA;
            }
            else
            {
                switch (decimalPlacesA)
                {
                    case 0: displayText = currentValue.ToString("F0"); break;
                    case 1: displayText = currentValue.ToString("F1"); break;
                    case 2: displayText = currentValue.ToString("F2"); break;
                    default: displayText = currentValue.ToString("F1"); break;
                }
            }

            textA.text = $"←{displayText}";
        }

        private void RefreshTextB()
        {
            float currentValue = sliderB.maxValue - sliderB.value;
            string displayText = "";
            string activeSpecialValueTextB = string.IsNullOrEmpty(_localizedSpecialValueTextB)
                ? specialValueTextB
                : _localizedSpecialValueTextB;

            if (useSpecialValueB && Mathf.Abs(currentValue - specialValueB) < 0.001f && !string.IsNullOrEmpty(activeSpecialValueTextB))
            {
                displayText = activeSpecialValueTextB;
            }
            else
            {
                switch (decimalPlacesB)
                {
                    case 0: displayText = currentValue.ToString("F0"); break;
                    case 1: displayText = currentValue.ToString("F1"); break;
                    case 2: displayText = currentValue.ToString("F2"); break;
                    default: displayText = currentValue.ToString("F1"); break;
                }
            }

            textB.text = $"{displayText}→";
        }

        public void _OnButtonAP()
        {
            sliderA.value += step;
        }

        public void _OnButtonAN()
        {
            sliderA.value -= step;
        }

        public void _OnButtonBP()
        {
            sliderB.value -= step;
        }

        public void _OnButtonBN()
        {
            sliderB.value += step;
        }
    }
}
