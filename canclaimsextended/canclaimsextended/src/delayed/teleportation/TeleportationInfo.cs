using System;
using canclaimsextended.src.delayed.cooldowns;
using Vintagestory.API.MathTools;

namespace canclaimsextended.src.delayed.teleportation
{
    /// <summary>A teleport waiting for its moment; paid up front, refunded when it does not happen.</summary>
    public class TeleportationInfo
    {
        public string PlayerUid { get; }
        public Vec3d Target { get; }
        /// <summary>Where the player stood when it was asked for; moving away cancels it.</summary>
        public Vec3d Start { get; }
        public long DueAt { get; }
        public CooldownType Cooldown { get; }
        public long CooldownSeconds { get; }
        // Checked again when due: the player may have lost the right to go there while waiting.
        readonly Func<bool> stillAllowed;
        readonly string refundAccount;
        decimal refundAmount;

        public TeleportationInfo(string playerUid, Vec3d target, Vec3d start, long dueAt,
            CooldownType cooldown, long cooldownSeconds, Func<bool> stillAllowed, string refundAccount, decimal refundAmount)
        {
            PlayerUid = playerUid;
            Target = target;
            Start = start;
            DueAt = dueAt;
            Cooldown = cooldown;
            CooldownSeconds = cooldownSeconds;
            this.stillAllowed = stillAllowed;
            this.refundAccount = refundAccount;
            this.refundAmount = refundAmount;
        }

        public bool IsStillAllowed() => stillAllowed == null || stillAllowed();

        /// <summary>Gives the payment back, at most once.</summary>
        public void Refund()
        {
            if (refundAccount == null || refundAmount <= 0) return;
            canclaimsextended.economyProvider.Deposit(refundAccount, refundAmount);
            refundAmount = 0;
        }
    }
}
