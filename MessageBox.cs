using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UniRx;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;

namespace Yuuta.Streaming
{
    public class MessageBox : MonoBehaviour
    {
        [Serializable]
        public class SCTierToColor
        {
            public int Tier;
            public Color color;
        }

        [SerializeField] private CanvasGroup _canvasGroup;

        [Header("顯示文字")]
        [SerializeField] private Text _displayNameText;
        [SerializeField] private TextMeshProUGUI _displayNameTextMeshPro;

        [Header("價格文字")]
        [SerializeField] private Text _priceText;
        [SerializeField] private TextMeshProUGUI _priceTextMeshPro;

        [Header("標題背景")]
        [SerializeField] private Image _titleBackgroundImage;

        [Header("標題 Tier 與顏色對應")]
        [SerializeField] private SCTierToColor[] _titleSCTierToColorMapping;


        [Header("內容文字")]
        [SerializeField] private Text _contentText;
        [SerializeField] private TextMeshProUGUI _contentTextMeshPro;

        [Header("內容背景")]
        [SerializeField] private Image _contentBackgroundImage;

        [Header("內容 Tier 與顏色對應")]
        [SerializeField] private SCTierToColor[] _contentSCTierToColorMapping;

        [Header("時間設定")]
        [SerializeField] private float _fadeInTime = 1f;
        [SerializeField] private float _keepTime = 3f;
        [SerializeField] private float _fadeOutTime = 1f;


        [Serializable]
        public struct MsgEvent
        {
            [Tooltip("SC事件最低觸發階級(-1不限制)")] public int MinTriggerTier;
            [Tooltip("SC事件最高觸發階級(-1不限制)")] public int MaxTriggerTier;
            [Tooltip("事件觸發後執行的事情")] public UnityEvent Event;
        }

        [SerializeField] private MsgEvent[] _messageEvents;

        private Queue<SuperChatMsg> _superChatMsgs = new Queue<SuperChatMsg>();

        public void EnqueueToRender(SuperChatMsg chatMessage)
        {
            _superChatMsgs.Enqueue(chatMessage);
        }

        private async void Start()
        {
            while (true)
            {
                if (!_superChatMsgs.Any())
                {
                    await UniTask.NextFrame();
                    continue;
                }

                var message = _superChatMsgs.Dequeue();

                if (_displayNameText != null)
                    _displayNameText.text = message.AuthorName;

                if (_displayNameTextMeshPro != null)
                    _displayNameTextMeshPro.text = message.AuthorName;

                if (_priceText != null)
                    _priceText.text = message.DisplayAmount;

                if (_priceTextMeshPro != null)
                    _priceTextMeshPro.text = message.DisplayAmount;

                _titleBackgroundImage.color =
                    _titleSCTierToColorMapping.FirstOrDefault(map => map.Tier == message.Tier)?.color ?? Color.white;

                if (_contentText != null)
                    _contentText.text = message.Msg;

                if (_contentTextMeshPro != null)
                    _contentTextMeshPro.text = message.Msg;

                _contentBackgroundImage.color =
                    _contentSCTierToColorMapping.FirstOrDefault(map => map.Tier == message.Tier)?.color ?? Color.white;

                foreach (var messageEvent in _messageEvents)
                {
                    var isTrigger = (messageEvent.MinTriggerTier < 0 || message.Tier >= messageEvent.MinTriggerTier) &&
                                    (messageEvent.MaxTriggerTier < 0 || message.Tier <= messageEvent.MaxTriggerTier);
                    if (!isTrigger)
                        continue;

                    messageEvent.Event?.Invoke();
                }

                _canvasGroup.alpha = 0f;
                await _canvasGroup.DOFade(1f, _fadeInTime).AsyncWaitForCompletion();
                await Observable.Timer(TimeSpan.FromSeconds(_keepTime));
                await _canvasGroup.DOFade(0f, _fadeOutTime).AsyncWaitForCompletion();
            }
        }
    }
}