using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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
}
