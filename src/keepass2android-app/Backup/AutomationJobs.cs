// This file is part of the 白い熊 鍵暗号 fork of Keepass2Android.
//
//   Keepass2Android is free software: you can redistribute it and/or modify
//   it under the terms of the GNU General Public License as published by
//   the Free Software Foundation, either version 3 of the License, or
//   (at your option) any later version.
//
//   Keepass2Android is distributed in the hope that it will be useful,
//   but WITHOUT ANY WARRANTY; without even the implied warranty of
//   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//   GNU General Public License for more details.
//
//   You should have received a copy of the GNU General Public License
//   along with Keepass2Android.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Concurrent;
using Android.OS;

namespace keepass2android.Backup
{
    /// <summary>
    /// One piece of work the data door has started: everything <see cref="AutomationDataService"/> needs,
    /// carried across in memory rather than through the Intent that starts the service.
    /// <para>
    /// <b>Why not Intent extras.</b> A <see cref="ParcelFileDescriptor"/> put into an Intent is duplicated
    /// by the system on delivery, and the copy's lifetime stops being ours to reason about. Handing the
    /// job through a map keyed by its id keeps exactly one open descriptor with exactly one owner — the
    /// runner, which closes it in a <c>finally</c>.
    /// </para>
    /// </summary>
    public sealed class AutomationJob
    {
        public string JobId;
        public ParcelFileDescriptor Fd;
        public bool Importing;
        public string Items;
        public string ReplyAction;
        public string ReplyPackage;
        public string ProgressAction;
    }

    /// <summary>
    /// The jobs the data door has started, and the flag each of them watches to stop.
    /// <para>
    /// What this owns is the mapping from the id a caller was handed to a cancellation it can act on,
    /// which must outlive the binder call that created it and be reachable from a service that never saw
    /// the caller.
    /// </para>
    /// </summary>
    public static class AutomationJobs
    {
        private static readonly ConcurrentDictionary<string, bool> Cancelled =
            new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

        public static string Begin()
        {
            string id = Guid.NewGuid().ToString();
            Cancelled[id] = false;
            return id;
        }

        /// <summary>
        /// Ask a job to stop. A no-op for an id that is finished or was never real — deliberately silent,
        /// because a cancel arriving after the work completed is the normal race, not an error, and
        /// answering it as one would make every well-behaved caller look broken.
        /// </summary>
        public static void Cancel(string jobId)
        {
            if (string.IsNullOrEmpty(jobId))
                return;
            Cancelled.TryUpdate(jobId, true, false);
        }

        /// <summary>Polled at write boundaries — never mid-write, so a cancelled archive is never half a file.</summary>
        public static bool IsCancelled(string jobId) =>
            !string.IsNullOrEmpty(jobId) && Cancelled.TryGetValue(jobId, out bool value) && value;

        public static void Finish(string jobId)
        {
            if (!string.IsNullOrEmpty(jobId))
                Cancelled.TryRemove(jobId, out _);
        }
    }
}
