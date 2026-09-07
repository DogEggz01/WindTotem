using System;
using System.Globalization;

namespace WindTotem
{
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
}
