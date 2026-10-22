using System;
using System.Collections.Generic;

namespace LargeFolder
{
    /// <summary>Changes only the visible order of existing entries in one group.</summary>
    public static class ItemOrdering
    {
        public static bool MoveRelative(FolderGroup group, string itemId, string targetItemId, bool after)
        {
            if (String.IsNullOrWhiteSpace(itemId) || String.IsNullOrWhiteSpace(targetItemId) ||
                String.Equals(itemId, targetItemId, StringComparison.OrdinalIgnoreCase)) return false;

            int source;
            int target;
            if (!TryFind(group, itemId, targetItemId, out source, out target)) return false;

            int destination = target + (after ? 1 : 0);
            if (source < destination) destination--;
            if (source == destination) return false;

            ItemEntry item = group.Items[source];
            group.Items.RemoveAt(source);
            group.Items.Insert(destination, item);
            return true;
        }

        public static bool MoveToEnd(FolderGroup group, string itemId)
        {
            if (String.IsNullOrWhiteSpace(itemId)) return false;
            int source;
            int unused;
            if (!TryFind(group, itemId, itemId, out source, out unused) || source == group.Items.Count - 1)
                return false;

            ItemEntry item = group.Items[source];
            group.Items.RemoveAt(source);
            group.Items.Add(item);
            return true;
        }

        private static bool TryFind(FolderGroup group, string itemId, string targetItemId, out int source, out int target)
        {
            source = -1;
            target = -1;
            if (group == null || group.Items == null) return false;

            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < group.Items.Count; index++)
            {
                ItemEntry entry = group.Items[index];
                if (entry == null || String.IsNullOrWhiteSpace(entry.Id) || !ids.Add(entry.Id)) return false;
                if (String.Equals(entry.Id, itemId, StringComparison.OrdinalIgnoreCase)) source = index;
                if (String.Equals(entry.Id, targetItemId, StringComparison.OrdinalIgnoreCase)) target = index;
            }
            return source >= 0 && target >= 0;
        }
    }
}
