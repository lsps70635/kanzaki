using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Firesplash.UnityAssets.TwitchIntegration;
using Firesplash.UnityAssets.TwitchIntegration.DataTypes.IRC;
using Firesplash.UnityAssets.TwitchIntegration.DataTypes.PubSub.Events;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.Serialization;
using VikingCrewDevelopment.Demos;
using Yuuta.Streaming;

namespace Yuuta
{

    public class YTAndTwitchStreamMsgAndMemberCtrl : MonoBehaviour
    {
        static IEnumerable<string> Split(string str, int chunkSize)
            => Enumerable.Range(0, str.Length / chunkSize + ((str.Length % chunkSize == 0) ? 0 : 1))
                .Select(i =>
                    (i * chunkSize + chunkSize > str.Length) ?
                        str.Substring(i * chunkSize) :
                        str.Substring(i * chunkSize, chunkSize));

        public class MemberModelManager
        {
            private Queue<string> _memberQueue;
            private IDictionary<string, GameObject> _memberModels;

            public MemberModelManager()
            {
                _memberQueue = new Queue<string>();
                _memberModels = new Dictionary<string, GameObject>();
            }

            public bool IsExisted(string memberId)
                => _memberModels.ContainsKey(memberId);

            public GameObject GetModel(string memberId)
            {
                if (!_memberModels.ContainsKey(memberId))
                    return null;

                return _memberModels[memberId];
            }

            public void SetModel(int countConstraint, string memberId, GameObject model)
            {
                while (_memberQueue.Count >= countConstraint)
                {
                    var destroyMemberId = _memberQueue.Dequeue();
                    if (!_memberModels.ContainsKey(destroyMemberId))
                        continue;

                    Destroy(_memberModels[destroyMemberId]);
                    _memberModels.Remove(destroyMemberId);
                }

                _memberQueue.Enqueue(memberId);
                _memberModels[memberId] = model;
            }
        }

        public static YTAndTwitchStreamMsgAndMemberCtrl Instance;

        [Header("[連線基本資料]")]
        [Tooltip("使用 Youtube?")] public bool _shouldOpenYoutube = true;
        [Tooltip("Youtube 直播房間ID")] public string YoutubeStreamID;
        [Tooltip("Youtube 所有可用的ApiKey")] public string[] YoutubeApiKey;
        [Tooltip("Youtube 留言刷新間隔(秒)")] public float YoutubeUpdateMsgSec = 1;

        [Tooltip("使用 Twitch?")] public bool _shouldOpenTwitch = true;
        [Tooltip("Twitch integration")] public TwitchIntegration Twitch;
        [Tooltip("Twitch 頻道名稱")] public string TwitchChannelName;
        [Tooltip("Twitch 頻道 ID")] public string TwitchChannelID;
        [Tooltip("Twitch Access Token")] public string TwitchAccessToken;

        [Tooltip("使用綠界?")] public bool _shouldOpenEcpay = true;
        [Tooltip("綠界動畫 ID")] public string EcpayBroadcastId;
        [Tooltip("綠界預設 Tier")] public int EcpayDefaultTier = 10;
        [Tooltip("綠界刷新間隔(秒)")] public float EcpayUpdateMsgSec = 5;

        [Header("[SuperChat 顯示工具]")]
        [Tooltip("顯示Box")]
        [SerializeField] private MessageBox _messageBox;

        [Header("[事件列表]")]
        [SerializeField] [Tooltip("事件列表")] private MsgEvent[] EventList;
        [SerializeField] [Tooltip("忠誠點數事件列表")] private PointRewardEvent[] _pointRewardEvents;
        [SerializeField] [Tooltip("小奇點事件列表")] private BitsEvent[] _bitsEvents;
        [SerializeField] [Tooltip("Twitch 訂閱事件列表")] private UnityEvent[] _twitchSubscribeEvents;
        [SerializeField] [Tooltip("Twitch 追隨事件列表")] private UnityEvent[] _twitchFollowEvents;


        [Header("[成員列表]")]
        [SerializeField] [Tooltip("成員可出現數量與位置")] private Vector3[] _positions;
        [SerializeField] [Tooltip("成員根節點")] private Transform _rootTransform;
        [SerializeField] [Tooltip("物件出現秒數")] private float _gameObjectSeconds;
        [SerializeField] [Tooltip("對話是幾個字換行")] private int _newlineTextLength = 10;
        [SerializeField] [Tooltip("會員共通訊息動作")] public MsgAnimatorArgumentControllerEvent[] _generalAnimatorControllerEvents;
        [SerializeField] [Tooltip("成員列表")] private MemberEvent[] _memberEvents;

        private IDictionary<string, Coroutine> _coroutines = new Dictionary<string, Coroutine>();

        private int ApiKeyIndex;

        private MemberModelManager _memberModelManager = new MemberModelManager();

        private int _nextIndex = 0;

        void NetKey()
        {
            if (++ApiKeyIndex >= YoutubeApiKey.Length) ApiKeyIndex = 0;
        }
        string GetApiKey()
        {
            return YoutubeApiKey[ApiKeyIndex];
        }

        public void RenderSuperChat(ChatMsg msg)
        {
            if (msg is SuperChatMsg superChatMsg)
            {
                _messageBox.EnqueueToRender(superChatMsg);
            }
        }

        public void ActiveEvent(ChatMsg msg)
        {
            int activeIndex = -1;
            for (int i = 0; i < EventList.Length; i++)
            {
                var e = EventList[i];
                if (e.OnlySuperChat)
                {
                    if (msg is SuperChatMsg)
                    {
                        var scmsg = msg as SuperChatMsg;
                        if ((e.TriggerMsg == "" || msg.Msg.IndexOf(e.TriggerMsg) > -1) && (e.MinTriggerTier < 0 || scmsg.Tier >= e.MinTriggerTier) && (e.MaxTriggerTier < 0 || scmsg.Tier <= e.MaxTriggerTier))
                        {
                            activeIndex = i;
                            break;
                        }
                    }
                }
                else if (msg.Msg.IndexOf(e.TriggerMsg) > -1)
                {
                    activeIndex = i;
                    break;
                }
            }
            if (activeIndex == -1) return;
            Debug.LogWarning($"<color=green>事件觸發[{activeIndex}]: {EventList[activeIndex].TriggerMsg}</color>");
            EventList[activeIndex].Event.Invoke();
        }

        public void ActivePointRewardEvent(PointReward reward)
        {
            int activeIndex = -1;
            for (int i = 0; i < _pointRewardEvents.Length; i++)
            {
                var e = _pointRewardEvents[i];
                if (reward.RewardName == e.TriggerPointRewardName)
                {
                    activeIndex = i;
                    break;
                }
            }
            if (activeIndex == -1) return;
            Debug.LogWarning($"<color=green>事件觸發[{activeIndex}]: {_pointRewardEvents[activeIndex].TriggerPointRewardName}</color>");
            _pointRewardEvents[activeIndex].Event.Invoke();
        }

        public void ActiveBitsEvent(Bits bits)
        {
            int activeIndex = -1;
            for (int i = 0; i < _bitsEvents.Length; i++)
            {
                var e = _bitsEvents[i];
                if ((e.MinBits < 0 || bits.Amount >= e.MinBits) &&
                    (e.MaxBits < 0 || bits.Amount <= e.MaxBits))
                {
                    activeIndex = i;
                    break;
                }
            }
            if (activeIndex == -1) return;
            Debug.LogWarning($"<color=green>事件觸發[{activeIndex}]: {_bitsEvents[activeIndex].MinBits} ~ {_bitsEvents[activeIndex].MaxBits}</color>");
            _bitsEvents[activeIndex].Event.Invoke();
        }

        public void ActiveMemberEvent(ChatMsg msg)
        {
            var memberEvent = _memberEvents
                .FirstOrDefault(memberEvent => (msg.Source == ChatMsg.SourceType.Youtube && msg.AuthorId == memberEvent.YoutubeTriggerMember) ||
                                               (msg.Source == ChatMsg.SourceType.Twitch && msg.AuthorId == memberEvent.TwitchTriggerMember));
            if (memberEvent == null)
                return;

            Debug.LogWarning($"<color=green>找到會員 {msg.AuthorName}({msg.AuthorId})</color>");
            var modelId = $"{memberEvent.YoutubeTriggerMember}:{memberEvent.TwitchTriggerMember}";
            GameObject model = _memberModelManager.GetModel(modelId);
            if (model == null)
            {
                model = Instantiate(
                    memberEvent.MemberPrefab,
                    _positions[_nextIndex],
                    Quaternion.Euler(memberEvent.InitRotation),
                    _rootTransform);

                _memberModelManager.SetModel(_positions.Length, modelId, model);
                _nextIndex = (_nextIndex + 1) % _positions.Length;
            }

            if (memberEvent.IsFillText)
            {
                var textMeshes = model.GetComponentsInChildren<TextMesh>(true);
                foreach (var textMesh in textMeshes)
                    textMesh.text = msg.Msg;

                var textMeshPros = model.GetComponentsInChildren<TextMeshPro>(true);
                foreach (var textMeshPro in textMeshPros)
                    textMeshPro.text = msg.Msg;

                var sayRandomThingsBehaviours = model.GetComponentsInChildren<SayRandomThingsBehaviour>(true);
                foreach (var sayRandomThingsBehaviour in sayRandomThingsBehaviours)
                {
                    sayRandomThingsBehaviour.thingsToSay = new string[]
                    {
                        string.Join("\n", Split(msg.Msg, _newlineTextLength).ToArray())
                    };
                }
            }

            if (memberEvent.ObjectNames != null && memberEvent.ObjectNames.Length > 0)
            {
                var children = memberEvent.ObjectNames.Select(name => model.transform.Find(name))
                    .Where(transform => transform != null);

                foreach (var child in children)
                    child.gameObject.SetActive(true);

                if (_coroutines.ContainsKey(msg.AuthorId))
                    StopCoroutine(_coroutines[msg.AuthorId]);

                _coroutines[msg.AuthorId] = StartCoroutine(_WaitAndDo(_gameObjectSeconds, () =>
                {
                    foreach (var child in children)
                        child.gameObject.SetActive(false);

                    _coroutines.Remove(msg.AuthorId);
                }));
            }

            var animatorControllerEvent = memberEvent.AnimatorControllerEvents
                .FirstOrDefault(animatorControllerEvent =>
                    string.IsNullOrEmpty(animatorControllerEvent.TriggerMsg) ||
                    msg.Msg.Contains(animatorControllerEvent.TriggerMsg));

            if (animatorControllerEvent == null)
            {
                animatorControllerEvent = _generalAnimatorControllerEvents
                    .FirstOrDefault(animatorControllerEvent =>
                        string.IsNullOrEmpty(animatorControllerEvent.TriggerMsg) ||
                        msg.Msg.Contains(animatorControllerEvent.TriggerMsg));

                if (animatorControllerEvent == null)
                    return;
            }

            Debug.LogWarning($"<color=green>找到事件對話: {msg.Msg}</color>");

            var animator = model.GetComponentsInChildren<Animator>()
                .FirstOrDefault(animator => animator.gameObject.CompareTag("CharacterController"));
            switch (animatorControllerEvent.Type)
            {
                case MsgAnimatorArgumentControllerEvent.ArgumentType.Integer:
                    animator.SetInteger(animatorControllerEvent.ArgumentName, animatorControllerEvent.ArgumentIntegerValue);
                    return;
                case MsgAnimatorArgumentControllerEvent.ArgumentType.Boolean:
                    animator.SetBool(animatorControllerEvent.ArgumentName, animatorControllerEvent.ArgumentBooleanValue);
                    return;
                case MsgAnimatorArgumentControllerEvent.ArgumentType.Float:
                    animator.SetFloat(animatorControllerEvent.ArgumentName, animatorControllerEvent.ArgumentFloatValue);
                    return;
            }

        }

        private IEnumerator _WaitAndDo(float seconds, Action action)
        {
            yield return new WaitForSeconds(seconds);
            action();
        }

        private void OnDestroy()
        {
            if (_coroutines == null)
                return;

            foreach (var coroutine in _coroutines)
            {
                StopCoroutine(coroutine.Value);
            }
            _coroutines.Clear();
        }

        private Coroutine _youtubeRun;
        private Coroutine _ecpayRun;
        private bool _isConnectedToTwitch = false;
        private bool NotFirst;
        private HashSet<string> _hasCatchedEcpayDonationIds = new HashSet<string>();

        private void OnEnable()
        {
            Instance = this;
            if (_shouldOpenYoutube)
                _youtubeRun = StartCoroutine(getChatID());

            if (_shouldOpenTwitch)
                _ConnectTwitch();

            if (_shouldOpenEcpay)
            {
                _hasCatchedEcpayDonationIds = new HashSet<string>();
                _ecpayRun = StartCoroutine(getEcpayMessage());
            }
        }

        public void OnDisable()
        {
            if (_youtubeRun != null) StopCoroutine(_youtubeRun);
            if (_isConnectedToTwitch) _DisconnectTwitch();
            if (_ecpayRun != null) StopCoroutine(_ecpayRun);
            NotFirst = false;
        }

        private void _ConnectTwitch()
        {
            Twitch.PubSub.Connect();
            _SubscribePubSub();
            Twitch.Chat.Connect(TwitchChannelName, TwitchAccessToken);
            Twitch.Chat.OnChatMessageReceived.AddListener(_OnReceiveIRCMessage);
            _isConnectedToTwitch = true;
        }

        private void _DisconnectTwitch()
        {
            _UnsubscribePubSub();
            Twitch.Chat.OnChatMessageReceived.RemoveListener(_OnReceiveIRCMessage);
            _isConnectedToTwitch = false;
        }

        private void _SubscribePubSub()
        {
            Twitch.PubSub.OnConnectionEstablished.AddListener(_OnConnectionEstablished);
            Twitch.PubSub.OnPointRewardEvent.AddListener(_OnReceivePointReward);
            Twitch.PubSub.OnBitsEvent.AddListener(_OnReceiveBits);
            Twitch.PubSub.OnNewFollowerEvent.AddListener(_OnFollow);
            Twitch.PubSub.OnSubscribeEvent.AddListener(_OnSubscribe);
        }

        private void _UnsubscribePubSub()
        {
            Twitch.PubSub.OnConnectionEstablished.RemoveListener(_OnConnectionEstablished);
            Twitch.PubSub.OnPointRewardEvent.RemoveListener(_OnReceivePointReward);
            Twitch.PubSub.OnBitsEvent.RemoveListener(_OnReceiveBits);
            Twitch.PubSub.OnNewFollowerEvent.RemoveListener(_OnFollow);
            Twitch.PubSub.OnSubscribeEvent.RemoveListener(_OnSubscribe);
        }

        private void _OnConnectionEstablished()
        {
            if (TwitchChannelID.Length > 0) //Did the user enter a channelID?
            {
                //The user entered a channel ID so we will use it and connect away
                Twitch.PubSub.SubscribeToBitsEventsV2(TwitchChannelID, TwitchAccessToken);
                Twitch.PubSub.SubscribeToChannelPointsEvents(TwitchChannelID, TwitchAccessToken);
                Twitch.PubSub.SubscribeToChannelSubscriptions(TwitchChannelID, TwitchAccessToken);
                Twitch.PubSub.SubscribeToNewFollowers(TwitchChannelID, TwitchAccessToken);
                Twitch.PubSub.SubscribeToHypeTrains(TwitchChannelID, TwitchAccessToken); //This one will cause a warning because it is experimental (undocumented PubSub topic). It also has two events!
                Twitch.PubSub.SubscribeToBitsBadgeNotifications(TwitchChannelID, TwitchAccessToken);
            }
            else
            {
                //The user DID NOT enter a channel ID so we will fetch it from twitch first (this is a free helper we provide due to high demand among customers)
                StartCoroutine(TwitchHelpers.GetChannelIDAndRun(TwitchAccessToken, (resultToken, resultChannelID, rawResult) => {
                    Twitch.PubSub.SubscribeToBitsEventsV2(resultChannelID, resultToken);
                    Twitch.PubSub.SubscribeToChannelPointsEvents(resultChannelID, resultToken);
                    Twitch.PubSub.SubscribeToChannelSubscriptions(resultChannelID, resultToken);
                    Twitch.PubSub.SubscribeToNewFollowers(resultChannelID, resultToken);
                    Twitch.PubSub.SubscribeToHypeTrains(resultChannelID, resultToken); //This one will cause a warning because it is experimental (undocumented PubSub topic). It also has two events!
                    Twitch.PubSub.SubscribeToBitsBadgeNotifications(resultChannelID, resultToken);
                }));
            }
        }

        private void _OnFollow(PubSubNewFollowerEvent e)
        {
            Debug.Log($"{e.Data.DisplayName}({e.Data.UserId}) follows this channel.");
            foreach (var followEvent in _twitchFollowEvents)
            {
                followEvent?.Invoke();
            }
        }

        private void _OnSubscribe(PubSubSubscribeEvent e)
        {
            Debug.Log($"{e.Data.DisplayName}({e.Data.UserId}) subscribes this channel.");
            foreach (var subscribeEvent in _twitchSubscribeEvents)
            {
                subscribeEvent?.Invoke();
            }
        }

        private void _OnReceiveIRCMessage(IRCMessage msg)
        {
            var chatMsg = new ChatMsg();
            chatMsg.Source = ChatMsg.SourceType.Twitch;
            chatMsg.AuthorId = msg.Sender.Id;
            chatMsg.AuthorName = msg.Sender.DisplayName;
            chatMsg.Msg = msg.Text;
            chatMsg.Do();
        }

        private void _OnReceivePointReward(PubSubPointRewardEvent e)
        {
            var pointReward = new PointReward();
            pointReward.AuthorId = e.Data.Redemption.User.Id;
            pointReward.AuthorName = e.Data.Redemption.User.DisplayName;
            pointReward.RewardName = e.Data.Redemption.Reward.Title;
            pointReward.Do();
        }

        private void _OnReceiveBits(PubSubBitsEvent e)
        {
            var bits = new Bits();
            bits.AuthorId = e.Data.UserId;
            bits.AuthorName = e.Data.UserName;
            bits.Amount = e.Data.BitsUsed;
            bits.Do();
        }

        private string ChatID;
        IEnumerator getChatID()
        {
            var url = $"https://www.googleapis.com/youtube/v3/videos?id={YoutubeStreamID}&key={GetApiKey()}&part=liveStreamingDetails";

            using (var req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();
                var json = SimpleJSON.JSON.Parse(req.downloadHandler.text);

                if (json["error"] != null)
                {
                    switch ((int)json["error"]["code"])
                    {
                        case 400:
                            Debug.LogError($"ApiKey[{ApiKeyIndex}]未知的Key(新創建的Key需要等待5min才會生效");
                            break;
                        case 403:
                            Debug.LogWarning($"ApiKey[{ApiKeyIndex}]Key已達使用上限嘗試下一個Key");
                            NetKey();
                            StartCoroutine(getChatID());
                            break;
                        default:
                            Debug.LogWarning($"未知錯誤");
                            break;
                    }

                    yield break;
                }

                if (json["pageInfo"]["totalResults"] == 0)
                {
                    Debug.LogError($"錯誤的VideoID,看看有沒有打錯");
                    yield break;
                }

                ChatID = json["items"][0]["liveStreamingDetails"]["activeLiveChatId"].ToString();
            }
            ChatID = ChatID.Replace("\"", "");
            Debug.Log("<color=green>連線直播成功!</color>");

            _youtubeRun = StartCoroutine(getChatMessageID());
        }

        private string pageToken;
        IEnumerator getChatMessageID()
        {
            var url = $"https://www.googleapis.com/youtube/v3/liveChat/messages?pageToken={pageToken}&liveChatId={ChatID}&key={GetApiKey()}&maxResults=2000&part=authorDetails,snippet";

            var msgList = new List<ChatMsg>();
            using (var req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();
                var json = SimpleJSON.JSON.Parse(req.downloadHandler.text);

                if (json["error"] != null)
                {
                    switch ((int)json["error"]["code"])
                    {
                        case 400:
                            Debug.LogWarning($"錯誤的pageToken,嘗試清空pageToken");
                            pageToken = null;
                            break;
                        case 403:
                            if (json["error"]["reason"] == "rateLimitExceeded")
                            {
                                Debug.LogWarning($"訪問頻率過於頻繁,嘗試降低訪問頻率");
                                YoutubeUpdateMsgSec += 1;
                                yield return new WaitForSeconds(YoutubeUpdateMsgSec);
                            }
                            else
                            if (json["error"]["reason"] == "quotaExceeded")
                            {
                                Debug.LogWarning($"ApiKey[{ApiKeyIndex}]Key已達使用上限嘗試下一個Key");
                                NetKey();
                            }
                            break;
                        default:
                            Debug.LogWarning($"未知錯誤");
                            break;
                    }
                    _youtubeRun = StartCoroutine(getChatMessageID());
                    yield break;
                }
                pageToken = json["nextPageToken"];
                var items = json["items"];
                for (int i = 0; i < items.Count; i++)
                {
                    ChatMsg msg;
                    var snippet = items[i]["snippet"];
                    if (snippet["type"] == "superChatEvent")
                    {
                        SuperChatMsg m = new SuperChatMsg();
                        m.AmountMicros = snippet["superChatDetails"]["amountMicros"];
                        m.Currency = snippet["superChatDetails"]["currency"];
                        m.DisplayAmount = snippet["superChatDetails"]["amountDisplayString"];
                        m.Tier = snippet["superChatDetails"]["tier"];
                        m.Msg = snippet["superChatDetails"]["userComment"];
                        msg = m;
                    }
                    else
                    {
                        msg = new ChatMsg();
                        msg.Msg = snippet["textMessageDetails"]["messageText"];
                    }

                    msg.Source = ChatMsg.SourceType.Youtube;
                    msg.AuthorId = items[i]["authorDetails"]["channelId"];
                    msg.AuthorName = items[i]["authorDetails"]["displayName"];

                    if (msg.Msg == null)
                    {
                        msg.Msg = "";
                    }

                    msgList.Add(msg);
                }
            }

            if (NotFirst)
            {
                foreach (var chatMsg in msgList)
                {
                    chatMsg.Do();
                }
            }
            else
                NotFirst = true;


            yield return new WaitForSeconds(YoutubeUpdateMsgSec);
            _youtubeRun = StartCoroutine(getChatMessageID());
        }

        IEnumerator getEcpayMessage()
        {
            var url = $"https://payment.ecpay.com.tw/Broadcaster/CheckDonate/{EcpayBroadcastId}";

            var msgList = new List<ChatMsg>();
            using (var req = UnityWebRequest.Post(url, string.Empty))
            {
                yield return req.SendWebRequest();
                var json = SimpleJSON.JSON.Parse(req.downloadHandler.text);

                foreach (var item in json.AsArray.Children)
                {
                    if (_hasCatchedEcpayDonationIds.Contains(item["donateid"]))
                        continue;

                    SuperChatMsg msg = new SuperChatMsg();
                    msg.Source = ChatMsg.SourceType.ECPay;
                    msg.AmountMicros = item["amount"] * 1000000;
                    msg.Currency = "NTD";
                    msg.DisplayAmount = $"NTD ${item["amount"]}";
                    msg.Tier = EcpayDefaultTier;
                    msg.Msg = item["msg"];
                    msg.AuthorId = item["donateid"];
                    msg.AuthorName = item["name"];

                    if (msg.Msg == null)
                    {
                        msg.Msg = "";
                    }

                    msgList.Add(msg);
                    _hasCatchedEcpayDonationIds.Add(item["donateid"]);
                }
            }

            if (NotFirst)
            {
                foreach (var chatMsg in msgList)
                {
                    chatMsg.Do();
                }
            }
            else
                NotFirst = true;


            yield return new WaitForSeconds(EcpayUpdateMsgSec);
            _ecpayRun = StartCoroutine(getEcpayMessage());
        }

    }

    public class ChatMsg
    {
        public enum SourceType
        {
            Youtube,
            Twitch,
            ECPay
        }

        public SourceType Source;
        public string Msg;
        public string AuthorName;
        public string AuthorId;

        public virtual void Print()
        {
            Debug.Log($"[{Source}]{AuthorName}({AuthorId}): {Msg}");
        }

        public virtual void Do()
        {
            Print();

            YTAndTwitchStreamMsgAndMemberCtrl.Instance.RenderSuperChat(this);
            YTAndTwitchStreamMsgAndMemberCtrl.Instance.ActiveEvent(this);
            YTAndTwitchStreamMsgAndMemberCtrl.Instance.ActiveMemberEvent(this);
        }
    }

    public class SuperChatMsg : ChatMsg
    {
        public long AmountMicros;
        public string Currency;
        public string DisplayAmount;
        public int Tier;

        public override void Print()
        {
            Debug.LogWarning($"[{Source}] [SC] [{Currency}: {AmountMicros / 1000000}]{AuthorName}: {Msg} Tier:{Tier}");
        }
    }

    public class PointReward
    {
        public string RewardName;
        public string AuthorName;
        public string AuthorId;

        public virtual void Print()
        {
            Debug.Log($"{AuthorName}({AuthorId}) Redeemed {RewardName}.");
        }

        public virtual void Do()
        {
            Print();
            YTAndTwitchStreamMsgAndMemberCtrl.Instance.ActivePointRewardEvent(this);
        }
    }

    public class Bits
    {
        public int Amount;
        public string AuthorName;
        public string AuthorId;
        public string Message;

        public virtual void Print()
        {
            Debug.Log($"{AuthorName}({AuthorId}) give {Amount} bits and say {Message}.");
        }

        public virtual void Do()
        {
            Print();
            YTAndTwitchStreamMsgAndMemberCtrl.Instance.ActiveBitsEvent(this);
        }
    }

    [Serializable]
    public struct MsgEvent
    {
        [Tooltip("事件觸發語句([空]不限制)")] public string TriggerMsg;
        [Tooltip("是否只有SC能觸發此事件")] public bool OnlySuperChat;
        [Tooltip("SC事件最低觸發階級(-1不限制)")] public int MinTriggerTier;
        [Tooltip("SC事件最高觸發階級(-1不限制)")] public int MaxTriggerTier;
        [Tooltip("事件觸發後執行的事情")] public UnityEvent Event;
    }

    [Serializable]
    public class MemberEvent
    {
        [Tooltip("名稱（僅標註用）")] public string Name;
        [FormerlySerializedAs("TriggerMember")] [Tooltip("Youtube 事件觸發會員 ID")] public string YoutubeTriggerMember;
        [Tooltip("Twitch 事件觸發會員 ID")] public string TwitchTriggerMember;
        [Tooltip("會員模型")] public GameObject MemberPrefab;
        [Tooltip("初始旋轉")] public Vector3 InitRotation;
        [Tooltip("開關物件之名稱")] public string[] ObjectNames;
        [Tooltip("是否填入文字？")] public bool IsFillText;
        [Tooltip("訊息動作")] public MsgAnimatorArgumentControllerEvent[] AnimatorControllerEvents;
    }

    [Serializable]
    public class PointRewardEvent
    {
        [Tooltip("事件觸發忠誠點數名稱")] public string TriggerPointRewardName;
        [Tooltip("事件觸發後執行的事情")] public UnityEvent Event;
    }

    [Serializable]
    public struct BitsEvent
    {
        [Tooltip("最低觸發小奇點數量(-1不限制)")] public int MinBits;
        [Tooltip("最高觸發小奇點數量(-1不限制)")] public int MaxBits;
        [Tooltip("事件觸發後執行的事情")] public UnityEvent Event;
    }

    [Serializable]
    public class MsgAnimatorArgumentControllerEvent
    {
        [Serializable]
        public enum ArgumentType
        {
            [Tooltip("整數")] Integer,
            [Tooltip("布林")] Boolean,
            [Tooltip("浮點數")] Float,
        }

        [Tooltip("事件觸發語句([空]不限制)")] public string TriggerMsg;
        [Tooltip("動畫參數名稱")] public string ArgumentName;
        [Tooltip("動畫參數值型態")] public ArgumentType Type;
        [Tooltip("動畫參數值(整數)")] public int ArgumentIntegerValue;
        [Tooltip("動畫參數值(布林數)")] public bool ArgumentBooleanValue;
        [Tooltip("動畫參數值(浮點數)")] public float ArgumentFloatValue;
    }


}