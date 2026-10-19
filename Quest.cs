using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using System;
using HarmonyLib;
using Object = UnityEngine.Object;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace WindTotem
{
    public sealed class WindTotemQuestArea : MonoBehaviour
    {
        public Transform Source;
        public bool IsGiver;
        public bool IsReturn;
        public WindTotemQuestArea Giver;
        internal static readonly Vector3 CrateLocalPosition = new Vector3(-1.4f, 1.3f, -0.42f);
        private readonly HashSet<Collider> players = new HashSet<Collider>();
        private QuestDialogue dialogue;
        private Rigidbody body;
        private Collider trigger;
        private bool accepting;
        private bool delivering;
        private int dialogueStage = -1;
        private float retry;
        private bool missingDialogueLogged;
        private bool offerOpen;
        private GameObject returnAreaObject;
        private float returnRetry;
        private bool WithinOfferDistance => Refs.observerMirror && Source &&
            (Refs.observerMirror.transform.position - Source.position).sqrMagnitude <= 25f;
        private void Awake() { body = GetComponent<Rigidbody>(); trigger = GetComponent<Collider>(); }
        private void Start() { EnsureReturnArea(); }
        private void EnsureReturnArea()
        {
            if (!IsGiver || returnAreaObject || Time.unscaledTime < returnRetry) return;
            returnRetry = Time.unscaledTime + 5f;
            var area = new GameObject("Wind Totem return delivery");
            try
            {
                area.SetActive(false);
                SceneManager.MoveGameObjectToScene(area, gameObject.scene);
                area.layer = 2;
                area.transform.position = Source.TransformPoint(CrateLocalPosition);
                var rb = area.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                var sphere = area.AddComponent<SphereCollider>();
                sphere.radius = 2f;
                sphere.isTrigger = true;
                var returnArea = area.AddComponent<WindTotemQuestArea>();
                returnArea.Source = Source;
                returnArea.IsReturn = true;
                returnArea.Giver = this;
                area.SetActive(true);
                returnAreaObject = area;
            }
            catch (Exception e)
            {
                area.SetActive(false);
                Destroy(area);
                WindTotemPlugin.Error("Return area setup failed; retrying: " + e);
            }
        }
        private void FixedUpdate()
        {
            if (!Source) { Destroy(gameObject); return; }
            if (IsGiver && !returnAreaObject) EnsureReturnArea();
            trigger.enabled = Source.gameObject.activeInHierarchy;
            if (!trigger.enabled || GameState.currentlyLoading) players.Clear();
            body.position = IsReturn ? Source.TransformPoint(CrateLocalPosition) : Source.position;
            // A separate unit-scale trigger keeps the dialogue/delivery radii and 3m cube exact.
            body.rotation = Quaternion.identity;
        }
        private bool Available => Source && Source.gameObject.activeInHierarchy && WindTotemPlugin.Ready &&
            QuestSave.Valid && !GameState.currentlyLoading && !GameState.justStarted && !GameState.recovering && !GameState.sleeping;
        private void Update()
        {
            if (IsReturn) return;
            players.RemoveWhere(c => !c || !c.gameObject.activeInHierarchy);
            int stage = QuestSave.Progress.Stage;
            if (dialogueStage != stage)
            {
                dialogue?.Dispose();
                dialogue = null;
                dialogueStage = stage;
                retry = 0f;
            }
            if (IsGiver)
            {
                if (!Available || (stage != QuestStages.NotAccepted && stage != QuestStages.Completed) || !WithinOfferDistance) offerOpen = false;
                else if (players.Count > 0) offerOpen = true;
            }
            bool show = Available && (IsGiver ? offerOpen :
                stage == QuestStages.Returning && Refs.observerMirror &&
                Vector3.Distance(Refs.observerMirror.transform.position, Source.position) < 6f);
            if (!show) { dialogue?.Hide(); return; }
            if (dialogue == null && Time.unscaledTime >= retry)
            {
                retry = Time.unscaledTime + 0.5f;
                dialogue = QuestDialogue.Create(transform, IsGiver ? (stage == QuestStages.Completed ? QuestText.Finish : QuestText.Offer) :
                    QuestText.Rock,
                    IsGiver && stage == QuestStages.NotAccepted ? (Action)Accept : null);
                if (dialogue == null && !missingDialogueLogged)
                {
                    missingDialogueLogged = true;
                    WindTotemPlugin.Error("Quest trigger reached, but the vanilla dialogue template is not yet available; retrying.");
                }
            }
            dialogue?.Show(Source, IsGiver);
        }
        private void OnTriggerEnter(Collider other) { Observe(other); }
        private void OnTriggerStay(Collider other) { Observe(other); }
        private void OnTriggerExit(Collider other) { players.Remove(other); }
        private void Observe(Collider other)
        {
            if (other.CompareTag("Player") && players.Add(other) && IsGiver)
                WindTotemPlugin.Log("Player entered Modular NPC (1) quest radius; ready=" + WindTotemPlugin.Ready +
                    ", stage=" + QuestSave.Progress.Stage + ", loading=" + GameState.currentlyLoading);
            if (IsGiver || !Available || delivering || QuestSave.Progress.Stage != (IsReturn ? QuestStages.Returning : QuestStages.Outbound)) return;
            var item = other.GetComponentInParent<ShipItem>();
            if (!item)
            {
                var proxy = other.GetComponentInParent<ItemRigidbody>();
                if (proxy) item = proxy.GetShipItem();
            }
            var save = item ? item.GetComponent<SaveablePrefab>() : null;
            if (!save || !QuestSave.Progress.Matches(save.prefabIndex, save.instanceId)) return;
            if (!IsReturn)
            {
                if (QuestSave.Progress.Visit(save.instanceId))
                {
                    QuestSave.Store();
                    WindTotemPlugin.Log("Rock of Despair visited; return crate " + save.instanceId + " to Dead Cove.");
                }
                return;
            }
            if (item.held) { item.OnDrop(); item.held.DropItem(); }
            StartCoroutine(Deliver(item, save.instanceId));
        }
        private void Accept()
        {
            if (!Available || accepting || !offerOpen || !WithinOfferDistance || QuestSave.Progress.Stage != QuestStages.NotAccepted) return;
            accepting = true;
            try
            {
                var item = QuestItems.SpawnAndCommit(608, Source.TransformPoint(CrateLocalPosition), QuestSave.Progress.Accept);
                var save = item.GetComponent<SaveablePrefab>();
                QuestSave.Store();
                dialogue?.Hide();
                StartCoroutine(QuestItems.ReleasePhysics(item));
                WindTotemPlugin.Log("Quest accepted; crate instance " + save.instanceId);
            }
            catch (Exception e) { WindTotemPlugin.Error("Quest acceptance failed: " + e); }
            finally { accepting = false; }
        }
        private IEnumerator Deliver(ShipItem crate, int crateId)
        {
            delivering = true;
            ShipItem reward = null;
            try
            {
                reward = QuestItems.SpawnAndCommit(609, Source.TransformPoint(CrateLocalPosition), id => QuestSave.Progress.Complete(crateId, id));
                int rewardId = reward.GetComponent<SaveablePrefab>().instanceId;
                QuestSave.Store();
                crate.DestroyItem();
                if (Giver) Giver.offerOpen = true;
                WindTotemPlugin.Log("Quest completed; reward instance " + rewardId);
            }
            catch (Exception e) { WindTotemPlugin.Error("Quest delivery failed: " + e); }
            if (reward) yield return QuestItems.ReleasePhysics(reward);
            delivering = false;
        }
    }

    public static class QuestStages
    {
        public const int NotAccepted = 0, Outbound = 1, Returning = 2, Completed = 3;
    }
    // Plain saved state. Instance IDs distinguish quest cargo from debug spawns.
    public sealed class QuestProgress
    {
        public int Stage { get; private set; }
        public int CrateId { get; private set; }
        public int RewardId { get; private set; }
        public static QuestProgress Reconcile(string data, int vanillaState)
        {
            if (data == null && vanillaState != 0)
                throw new FormatException("Quest identity data is missing but a vanilla quest flag exists; quest disabled to prevent repeating it.");
            var progress = data == null ? new QuestProgress() : Decode(data);
            if (vanillaState == -5 && progress.Stage != QuestStages.Completed)
                throw new FormatException("Quest completion records disagree; quest disabled to prevent another reward.");
            return progress;
        }
        public bool Accept(int crateId)
        {
            if (Stage != QuestStages.NotAccepted || crateId <= 0) return false;
            CrateId = crateId;
            Stage = QuestStages.Outbound;
            return true;
        }
        public bool Matches(int prefab, int id) => (Stage == QuestStages.Outbound || Stage == QuestStages.Returning) && prefab == 608 && id == CrateId;
        public bool Visit(int crateId)
        {
            if (Stage != QuestStages.Outbound || !Matches(608, crateId)) return false;
            Stage = QuestStages.Returning;
            return true;
        }
        public bool Complete(int crateId, int rewardId)
        {
            if (Stage != QuestStages.Returning || !Matches(608, crateId) || rewardId <= 0) return false;
            RewardId = rewardId;
            Stage = QuestStages.Completed;
            return true;
        }
        public string Encode() => string.Join("|", "2", Stage.ToString(CultureInfo.InvariantCulture),
            CrateId.ToString(CultureInfo.InvariantCulture), RewardId.ToString(CultureInfo.InvariantCulture));
        public static QuestProgress Decode(string data)
        {
            var p = data.Split('|');
            if (p.Length != 4 || (p[0] != "1" && p[0] != "2") || !int.TryParse(p[1], out int stage) ||
                !int.TryParse(p[2], out int crate) || !int.TryParse(p[3], out int reward) ||
                stage < 0 || stage > (p[0] == "1" ? 2 : 3) || crate < 0 || reward < 0 ||
                (stage == QuestStages.NotAccepted && (crate != 0 || reward != 0)) ||
                (stage >= 1 && crate == 0) ||
                (stage < (p[0] == "1" ? 2 : 3) && reward != 0) ||
                (stage == (p[0] == "1" ? 2 : 3) && reward == 0))
                throw new FormatException("Invalid Wind Totem quest state; quest disabled to protect saved progress.");
            return new QuestProgress { Stage = p[0] == "1" && stage == QuestStages.Returning ? 3 : stage, CrateId = crate, RewardId = reward };
        }
    }

    internal static class QuestSave
    {
        private const string Key = "DogEggz.WindTotem.Quest.v1";
        internal static QuestProgress Progress = new QuestProgress();
        internal static bool Valid = true;
        internal static void Reset() { Progress = new QuestProgress(); Valid = true; }
        internal static void Load()
        {
            Reset();
            try
            {
                string data = null;
                GameState.modData?.TryGetValue(Key, out data);
                var states = Quests.instance ? Quests.instance.currentQuests : null;
                Progress = QuestProgress.Reconcile(data, states != null && states.Length > 1 ? states[1] : 0);
                SyncVanilla();
            }
            catch (Exception e) { Valid = false; WindTotemPlugin.Error(e.Message); }
        }
        internal static void Store()
        {
            if (!Valid) return;
            if (GameState.modData == null) GameState.modData = new Dictionary<string, string>();
            GameState.modData[Key] = Progress.Encode();
            SyncVanilla();
        }
        private static void SyncVanilla()
        {
            if (!Valid || !Quests.instance) return;
            var states = Quests.instance.currentQuests ?? new int[0];
            if (states.Length < 2) Array.Resize(ref states, 2);
            states[1] = Progress.Stage == QuestStages.Completed ? -5 : Progress.Stage == QuestStages.Returning ? -2 : Progress.Stage == QuestStages.Outbound ? -1 : 0;
            Quests.instance.currentQuests = states;
        }
    }

    [HarmonyPatch(typeof(SaveLoadManager), "Awake")]
    internal static class QuestNewSession { private static void Prefix() { QuestSave.Reset(); } }
    [HarmonyPatch(typeof(SaveLoadManager), nameof(SaveLoadManager.LoadGame))]
    internal static class QuestLoading
    {
        private static void Prefix()
        {
            QuestSave.Reset();
            // Vanilla skips assigning modData for old saves with a null dictionary.
            GameState.modData?.Remove("DogEggz.WindTotem.Quest.v1");
        }
    }
    [HarmonyPatch(typeof(SaveLoadManager), nameof(SaveLoadManager.LoadModData))]
    internal static class QuestLoad { private static void Postfix() { QuestSave.Load(); } }
    [HarmonyPatch(typeof(SaveLoadManager), nameof(SaveLoadManager.SaveModData))]
    internal static class QuestStore { private static void Prefix() { QuestSave.Store(); } }

    internal static class QuestItems
    {
        internal static ShipItem SpawnAndCommit(int index, Vector3 position, Func<int, bool> commit)
        {
            GameObject root = null;
            SaveablePrefab save = null;
            try
            {
                root = UnityEngine.Object.Instantiate(PrefabsDirectory.instance.directory[index], position,
                    Quaternion.identity, FloatingOriginManager.instance.transform);
                var item = root.GetComponent<ShipItem>();
                save = root.GetComponent<SaveablePrefab>();
                if (!item || !save) throw new InvalidOperationException("Quest prefab is missing item/save components.");
                item.sold = true;
                item.nailed = false;
                save.RegisterToSave();
                if (!commit(save.instanceId)) throw new InvalidOperationException("Quest transition rejected; removing uncommitted item.");
                return item;
            }
            catch
            {
                try { if (save && SaveLoadManager.instance) save.Unregister(); }
                finally { if (root) { root.SetActive(false); UnityEngine.Object.Destroy(root); } }
                throw;
            }
        }
        internal static IEnumerator ReleasePhysics(ShipItem item)
        {
            // Vanilla creates a physics proxy/collider asynchronously and holds it
            // kinematic for the first six physics frames. Never make the visual body dynamic.
            for (int i = 0; i < 8; i++) yield return new WaitForFixedUpdate();
            if (!item || !item.itemRigidbodyC || item.held || item.nailed || GameState.currentlyLoading) yield break;
            var proxy = item.itemRigidbodyC;
            proxy.attached = false;
            proxy.disableCol = false;
            proxy.inStove = false;
            var rb = proxy.GetBody();
            if (rb && proxy.GetComponent<Collider>()) { rb.useGravity = true; rb.isKinematic = false; rb.WakeUp(); }
        }
    }

    // The actual island's update is authoritative: it also covers islands which
    // were already loaded before a scene-event subscription was established.
    [HarmonyPatch(typeof(IslandSceneryScene), "Update")]
    internal static class QuestIslandInstaller
    {
        [HarmonyPostfix]
        internal static void Postfix(IslandSceneryScene __instance)
        {
            int index = __instance.parentIslandIndex;
            if (index != 37 && index != 30) return;
            var status = __instance.GetComponent<QuestIslandStatus>();
            if (!status) status = __instance.gameObject.AddComponent<QuestIslandStatus>();
            if (status.Complete || Time.unscaledTime < status.NextAttempt) return;
            status.NextAttempt = Time.unscaledTime + 1f;
            GameObject area = null;
            try
            {
                Transform source = FindTarget(__instance.transform, index);
                if (!source)
                {
                    if (!status.ReportedMissing)
                    {
                        status.ReportedMissing = true;
                        WindTotemPlugin.Error("Quest anchor not found on island " + index + " under " + __instance.name + "; retrying.");
                    }
                    return;
                }
                var marker = source.GetComponent<QuestAnchorMarker>();
                if (marker && marker.Area) { status.Complete = true; return; }
                area = new GameObject(index == 37 ? "Wind Totem quest giver" : "Wind Totem quest delivery");
                area.SetActive(false);
                SceneManager.MoveGameObjectToScene(area, source.gameObject.scene);
                area.transform.position = source.position;
                var body = area.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                // Trigger volume should not intercept the player's pointing ray.
                area.layer = 2;
                Collider trigger;
                if (index == 37) { var sphere = area.AddComponent<SphereCollider>(); sphere.radius = 4f; trigger = sphere; }
                else { var box = area.AddComponent<BoxCollider>(); box.size = Vector3.one * 3f; trigger = box; }
                trigger.isTrigger = true;
                var quest = area.AddComponent<WindTotemQuestArea>();
                quest.Source = source;
                quest.IsGiver = index == 37;
                area.SetActive(true);
                if (!marker) marker = source.gameObject.AddComponent<QuestAnchorMarker>();
                marker.Area = area;
                status.Complete = true;
                WindTotemPlugin.Log("Installed " + area.name + " on island " + index + " at " + source.name +
                    "; local=" + __instance.transform.InverseTransformPoint(source.position) +
                    (index == 37 ? "; radius=4m." : "; size=3x3x3m."));
            }
            catch (Exception e)
            {
                if (area) { area.SetActive(false); UnityEngine.Object.Destroy(area); }
                status.Complete = false;
                status.NextAttempt = Time.unscaledTime + 10f;
                WindTotemPlugin.Error("Quest trigger installation failed on island " + index + ": " + e);
            }
        }

        internal static Transform FindTarget(Transform island, int index)
        {
            if (index != 37 && index != 30) return null;
            foreach (var candidate in island.GetComponentsInChildren<Transform>(true))
            {
                if (index == 37 && candidate.name == "Modular NPC (1)" &&
                    Vector3.Distance(island.InverseTransformPoint(candidate.position),
                        new Vector3(-12.699f, 45.577f, 23.885f)) < 0.5f) return candidate;
                if (index == 30 && candidate.name == "building_arab_obelisk_small") return candidate;
            }
            return null;
        }
    }

    public sealed class QuestIslandStatus : MonoBehaviour
    {
        public bool Complete;
        public bool ReportedMissing;
        public float NextAttempt;
    }
    public sealed class QuestAnchorMarker : MonoBehaviour { public GameObject Area; }

    // Uses the same vanilla TavernRumorsDude speech UI as Postal Expansion.
    // All scale/layout changes apply to our clone, never the source NPC or Postal UI.
    internal sealed class QuestDialogue
    {
        private static GameObject template;
        private static string textPath;
        private static string buttonPath;
        private readonly GameObject panel;
        private readonly Transform bubble;
        private QuestDialogue(GameObject panel, Transform bubble) { this.panel = panel; this.bubble = bubble; }

        internal static void Capture(TavernRumorsDude source)
        {
            if (template || !source || !source.speechUI || !source.text || !source.drinkButton) return;
            textPath = Path(source.speechUI.transform, source.text.transform);
            buttonPath = Path(source.speechUI.transform, source.drinkButton.transform);
            template = Object.Instantiate(source.speechUI);
            template.name = "Wind Totem dialogue template";
            template.SetActive(false);
            template.transform.SetParent(null, false);
            template.transform.localScale = Vector3.one;
            // Remove copied GPButtonInterface and its pointer outline. Its source
            // reference still points at the tavern NPC and must never be activated.
            foreach (var script in template.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(script);
            if (Application.isPlaying) Object.DontDestroyOnLoad(template);
        }

        internal static QuestDialogue Create(Transform parent, string content, Action accept)
        {
            if (!template)
                foreach (var source in Resources.FindObjectsOfTypeAll<TavernRumorsDude>())
                    if (source.gameObject.scene.IsValid()) { Capture(source); if (template) break; }
            if (!template) return null;
            var panel = Object.Instantiate(template, parent, false);
            panel.name = "Wind Totem quest dialogue";
            panel.transform.localPosition = Vector3.zero;
            panel.transform.localRotation = Quaternion.identity;
            panel.transform.localScale = Vector3.one;
            var text = panel.transform.Find(textPath).GetComponent<TextMesh>();
            var bubble = text.transform.parent;
            var graphics = bubble.Find("gfx");
            var button = panel.transform.Find(buttonPath);
            panel.SetActive(true);
            button.gameObject.SetActive(false);
            // Measure rendered text instead of scaling the font. Existing bubble
            // padding is preserved while height grows to fit the entire paragraph.
            var textRenderer = text.GetComponent<Renderer>();
            Vector3 oldTextSize = textRenderer.bounds.size;
            Vector3 oldBubbleSize = graphics.GetComponent<Renderer>().bounds.size;
            string wrapped = Wrap(content, 40);
            text.font.RequestCharactersInTexture(wrapped, text.fontSize, text.fontStyle);
            text.text = wrapped;
            Vector3 newTextSize = textRenderer.bounds.size;
            Vector3 scale = graphics.localScale;
            scale.x *= Mathf.Max(0.35f, (oldBubbleSize.x + newTextSize.x - oldTextSize.x) / Mathf.Max(0.001f, oldBubbleSize.x));
            scale.y *= Mathf.Max(1f, (oldBubbleSize.y + newTextSize.y - oldTextSize.y) / Mathf.Max(0.001f, oldBubbleSize.y));
            graphics.localScale = scale;
            foreach (var collider in panel.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            if (accept != null)
            {
                button.gameObject.SetActive(true);
                button.GetComponentInChildren<TextMesh>(true).text = "Accept";
                var clickTarget = button.GetComponentInChildren<Collider>(true);
                clickTarget.enabled = true;
                clickTarget.gameObject.layer = 0;
                clickTarget.gameObject.AddComponent<QuestAcceptButton>().Accepted = accept;
                var buttonPosition = button.localPosition;
                buttonPosition.y = bubble.localPosition.y - graphics.GetComponent<Renderer>().bounds.size.y * 0.5f -
                    clickTarget.GetComponent<Renderer>().bounds.size.y * 0.5f - 0.08f;
                button.localPosition = buttonPosition;
            }
            panel.SetActive(false);
            return new QuestDialogue(panel, bubble);
        }

        internal void Show(Transform anchor, bool giver)
        {
            if (!panel || !Refs.observerMirror) return;
            var observer = Refs.observerMirror.transform;
            Vector3 toward = Vector3.ProjectOnPlane(observer.position - anchor.position, Vector3.up).normalized;
            Vector3 position = anchor.position + toward * 0.75f;
            position.y = (giver ? observer.position.y + 1.2f : Mathf.Max(anchor.position.y + 0.8f, observer.position.y + 0.5f) + 0.3f) - bubble.localPosition.y;
            panel.transform.SetPositionAndRotation(position, observer.rotation);
            panel.SetActive(true);
        }
        internal void Hide() { if (panel) panel.SetActive(false); }
        internal void PlaceServiceButton(Transform button, Quaternion faceRotation, Renderer[] renderers)
        {
            var bounds = bubble.Find("gfx").GetComponent<Renderer>().bounds;
            // Native transport graphics and text face 180 degrees away from
            // their button group. Preserve that authored facing and scale.
            button.rotation = panel.transform.rotation * Quaternion.Inverse(faceRotation);
            if (renderers.Length == 0) return;
            var buttonBounds = renderers[0].bounds;
            foreach (var renderer in renderers) buttonBounds.Encapsulate(renderer.bounds);
            button.position += new Vector3(bounds.center.x - buttonBounds.center.x,
                bounds.min.y - 0.08f - buttonBounds.max.y, bounds.center.z - buttonBounds.center.z);
        }
        internal void Dispose() { if (panel) Object.Destroy(panel); }

        private static string Path(Transform root, Transform child)
        {
            var names = new List<string>();
            while (child && child != root) { names.Add(child.name); child = child.parent; }
            if (child != root) throw new InvalidOperationException("Dialogue element is outside speech UI.");
            names.Reverse();
            return string.Join("/", names);
        }
        private static string Wrap(string value, int width)
        {
            value = value.TrimStart();
            if (value.Length <= width) return value;
            int cut = value.LastIndexOf(' ', width);
            if (cut < 1) cut = width;
            return value.Substring(0, cut) + "\n" + Wrap(value.Substring(cut), width);
        }
    }

    public sealed class QuestAcceptButton : GoPointerButton
    {
        public Action Accepted;
        public override void OnActivate() { Accepted?.Invoke(); }
    }

    [HarmonyPatch(typeof(TavernRumorsDude), "Awake")]
    internal static class QuestDialogueCapture
    {
        private static void Postfix(TavernRumorsDude __instance) { QuestDialogue.Capture(__instance); }
    }

    public static class QuestText
    {
        public const string Offer = "The monks at the Sanctuary sent word of a recent vision: a dhow stranded far to the north, at the edge of the Emerald Sea. They asked me to send help to its unfortunate sailor. Please, take this crate to him. Its contents may yet be of use. Ask around Happy Bay harbour, someone there may know where he went.";
        public const string Finish = "Oh… then I suppose the priest from Neverdin has already seen to him. You sailed to the end of the Emerald sea just to help a stranger. That was kind of you. Keep the totem. I hope it serves you well.";
        public const string Rock = "Your fellow sailor will no longer need this. You should bring this back to Dead Cove.";
        public const string PortLine = "No, I don’t recall a boat like that. Try asking Lee, the cart loader sitting outside. He might have seen it.";
        public const string LeeLine = "Oh, yes, I remember that boat! You don’t often see a dhow in these waters. I think he headed east. Now, do you need anything hauled? My services are well worth the coin!";
    }

    internal static class HappyBayClues
    {
        internal static bool Eligible => QuestSave.Valid && QuestSave.Progress.Stage == QuestStages.Outbound &&
            !GameState.currentlyLoading && !GameState.justStarted && !GameState.sleeping && !GameState.recovering;
        internal static bool IsLee(CargoTransportDude dude) => dude && dude.carrierIndex == 18 && dude.name == "transport dude";
        internal static void Attach(GameObject npc, CargoTransportDude transport = null)
        {
            var clue = npc.GetComponent<HappyBayClue>();
            if (!clue) clue = npc.AddComponent<HappyBayClue>();
            clue.Transport = transport;
        }
    }

    [HarmonyPatch(typeof(PortDude), "Awake")]
    internal static class HappyBayPortInstall
    {
        private static void Postfix(PortDude __instance)
        {
            if (__instance.GetPort() && __instance.GetPort().portIndex == 20) HappyBayClues.Attach(__instance.gameObject);
        }
    }
    [HarmonyPatch(typeof(IslandSceneryScene), "Update")]
    internal static class HappyBayLeeInstall
    {
        private static void Postfix(IslandSceneryScene __instance)
        {
            if (__instance.parentIslandIndex != 18) return;
            var status = __instance.GetComponent<HappyBayInstalled>();
            if (!status) status = __instance.gameObject.AddComponent<HappyBayInstalled>();
            if (status.Complete || Time.unscaledTime < status.NextAttempt) return;
            status.NextAttempt = Time.unscaledTime + 1f;
            foreach (var dude in __instance.GetComponentsInChildren<CargoTransportDude>(true))
                if (HappyBayClues.IsLee(dude))
                {
                    HappyBayClues.Attach(dude.gameObject, dude);
                    status.Complete = true;
                    return;
                }
        }
    }
    public sealed class HappyBayInstalled : MonoBehaviour { public bool Complete; public float NextAttempt; }

    // The original short transport trigger must not close the extended 4m/5m clue.
    [HarmonyPatch(typeof(CargoTransportDude), "OnTriggerEnter")]
    internal static class HappyBayTransportEnter
    {
        private static bool Prefix(CargoTransportDude __instance) => !HappyBayClues.IsLee(__instance) || !HappyBayClues.Eligible;
    }
    [HarmonyPatch(typeof(CargoTransportDude), "OnTriggerExit")]
    internal static class HappyBayTransportExit
    {
        private static bool Prefix(CargoTransportDude __instance) => !HappyBayClues.IsLee(__instance) || !HappyBayClues.Eligible;
    }

    public sealed class HappyBayClue : MonoBehaviour
    {
        public CargoTransportDude Transport;
        private QuestDialogue dialogue;
        private bool open;
        private float retry;
        private void Update()
        {
            float distance = Refs.observerMirror ? Vector3.Distance(transform.position, Refs.observerMirror.transform.position) : float.PositiveInfinity;
            bool next = HappyBayClues.Eligible && distance <= (open ? 5f : Transport ? 4f : 3f);
            if (!next)
            {
                if (open && Transport && CargoCarrierUI.instance) CargoCarrierUI.instance.HideUI();
                open = false;
                dialogue?.Hide();
                return;
            }
            if (Transport)
            {
                if (!open && CargoCarrierUI.instance) CargoCarrierUI.instance.ShowUI(transform, Transport.carrierIndex);
                open = CargoCarrierUI.instance;
            }
            else
            {
                open = true;
                if (dialogue == null && Time.unscaledTime >= retry)
                {
                    retry = Time.unscaledTime + .5f;
                    dialogue = QuestDialogue.Create(transform, QuestText.PortLine, null);
                }
                dialogue?.Show(transform, true);
            }
        }
        private void OnDisable()
        {
            if (open && Transport && CargoCarrierUI.instance) CargoCarrierUI.instance.HideUI();
            open = false;
            dialogue?.Hide();
        }
    }

    [HarmonyPatch(typeof(CargoCarrierUI), nameof(CargoCarrierUI.ShowUI))]
    internal static class HappyBayServiceShow
    {
        private static void Prefix(CargoCarrierUI __instance) { __instance.GetComponent<HappyBayServiceClue>()?.Restore(); }
        private static void Postfix(CargoCarrierUI __instance, Transform transportDude, int carrierIndex)
        {
            if (carrierIndex != 18 || !HappyBayClues.Eligible || !transportDude || transportDude.name != "transport dude") return;
            var clue = __instance.GetComponent<HappyBayServiceClue>();
            if (!clue) clue = __instance.gameObject.AddComponent<HappyBayServiceClue>();
            clue.Source = transportDude;
        }
    }
    [HarmonyPatch(typeof(CargoCarrierUI), nameof(CargoCarrierUI.HideUI))]
    internal static class HappyBayServiceHide
    {
        private static void Prefix(CargoCarrierUI __instance) { __instance.GetComponent<HappyBayServiceClue>()?.Restore(); }
    }

    public sealed class HappyBayServiceClue : MonoBehaviour
    {
        public Transform Source;
        private QuestDialogue dialogue;
        private Transform button;
        private Vector3 buttonPosition;
        private Quaternion buttonRotation;
        private Quaternion buttonFaceRotation;
        private Renderer[] buttonRenderers;
        private readonly Dictionary<Renderer, bool> originalRenderers = new Dictionary<Renderer, bool>();
        private void LateUpdate()
        {
            if (!Source) return;
            if (!HappyBayClues.Eligible) { Restore(); return; }
            if (dialogue == null)
            {
                var ui = GetComponent<CargoCarrierUI>();
                var pointer = GetComponentInChildren<CargoCarrierButton>(true);
                if (!pointer || !ui.buttonText) return;
                // Locate the original button group including its label and collider.
                button = pointer.transform;
                while (button != transform && !ui.buttonText.transform.IsChildOf(button)) button = button.parent;
                if (button == transform) { button = null; return; }
                var nativeRenderers = GetComponentsInChildren<Renderer>(true);
                dialogue = QuestDialogue.Create(transform, QuestText.LeeLine, null);
                if (dialogue == null) { button = null; return; }
                buttonPosition = button.localPosition;
                buttonRotation = button.localRotation;
                buttonFaceRotation = Quaternion.Inverse(button.rotation) * ui.buttonText.transform.rotation;
                buttonRenderers = button.GetComponentsInChildren<Renderer>();
                foreach (var renderer in nativeRenderers)
                    if (!renderer.transform.IsChildOf(button))
                    {
                        // Only native renderers: the dialogue clone is not part of this snapshot.
                        originalRenderers[renderer] = renderer.enabled;
                        renderer.enabled = false;
                    }
            }
            dialogue.Show(Source, true);
            dialogue.PlaceServiceButton(button, buttonFaceRotation, buttonRenderers);
        }
        public void Restore()
        {
            Source = null;
            if (button) { button.localPosition = buttonPosition; button.localRotation = buttonRotation; }
            foreach (var pair in originalRenderers) if (pair.Key) pair.Key.enabled = pair.Value;
            originalRenderers.Clear();
            button = null;
            buttonRenderers = null;
            dialogue?.Hide();
            dialogue?.Dispose();
            dialogue = null;
        }
        private void OnDisable() { Restore(); }
    }
}
