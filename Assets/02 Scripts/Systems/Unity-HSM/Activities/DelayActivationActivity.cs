using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace HSM {
    public class DelayActivationActivity : Activity {
        public float seconds = 0.2f;

        public override async Task ActivateAsync(CancellationToken ct) {
            Debug.Log($"Activating {GetType().Name} (mode={this.Mode}) after {seconds} seconds");
            await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
            await base.ActivateAsync(ct);
        }
    }

    public class DelayDeactivationActivity : Activity {
        public float seconds = 0.2f;

        public override async Task DeactivateAsync(CancellationToken ct) {
            Debug.Log($"Deactivating {GetType().Name} (mode={this.Mode}) after {seconds} seconds");
            await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
            await base.DeactivateAsync(ct);
        }
    }

    public class ShieldDelayDeactivationActivity : Activity {
        public PlayerContext ctx;

        public override async Task DeactivateAsync(CancellationToken ct) {
            if (ctx != null)
            {
                Debug.Log($"Deactivating {GetType().Name} (mode={this.Mode}) after {ctx.heftTime} seconds");
                await Task.Delay(TimeSpan.FromSeconds(ctx.heftTime), ct);
            }
            await base.DeactivateAsync(ct);
        }
    }
}