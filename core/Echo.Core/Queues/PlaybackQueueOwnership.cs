using Echo.Core.Protocol;

namespace Echo.Core.Queues;

public sealed partial class PlaybackQueueStorage
{
    private void EnsureApplicationOwner(PlaybackQueueRequest request)
    {
        var owner=Convert.ToString(Scalar("SELECT owner FROM core_queue_owner WHERE id=1"));
        // Unclaimed storage is also used by offline import/inspection tools.
        // Once claimed, neither an old protocol client nor the JS V1 writer
        // may access it without the application's current lease.
        if(request.ApplicationToken is not {} token)
        {
            if(owner=="core")throw new InvalidOperationException("Playback queue is owned by Core.");
            return;
        }
        if(token.Length is <16 or >128)throw new ArgumentException("Concrete application queue lease required.");
        var current=Convert.ToString(Scalar("SELECT application_token FROM core_queue_owner WHERE id=1"));
        if(owner=="core" && current==token && request.Read?.Operation!="claim")return;
        // The final rollback response can be lost after ownership was released.
        // Its former lease may confirm receipts, but cannot read/edit the queue.
        if(owner=="legacy" && current==token && request.Read?.Operation=="receipt")return;
        if(request.Read?.Operation is not ("claim" or "releaseUnmigrated"))throw new InvalidOperationException("Playback queue application lease is not claimed.");
        using var transaction=Begin();
        if(current!=token)ClearSelection();
        if(owner=="legacy")
        {
            foreach(var id in new[]{"active","original","undo","migration","migration-original"})RemoveSession(id);
            Run("DELETE FROM core_queue_temporary;DELETE FROM core_queue_migration;DELETE FROM core_queue_legacy_snapshot;DELETE FROM core_queue_receipts;DELETE FROM core_queue_undo_selection_items");
            _migrationReady=false;
        }
        var live=request.Read?.LiveStagingIds??[];
        if(live.Count>2)throw new ArgumentException("Too many active queue replacements.");
        // Main's single-instance application owns this lease. It is stable
        // across Core restart and library switches, and changes on Main restart.
        using(var command=Command("SELECT id FROM core_queue_sessions WHERE kind='staging' AND id NOT LIKE 'migration%'"))
        using(var reader=command.ExecuteReader())
        {
            var abandoned=new List<string>(2);
            while(reader.Read())
            {
                var id=reader.GetString(0);
                if(current!=token || !live.Contains(id))abandoned.Add(id);
            }
            reader.Close();
            foreach(var id in abandoned)RemoveSession(id);
        }
        Run("INSERT INTO core_queue_owner VALUES(1,'core',$p0) ON CONFLICT(id) DO UPDATE SET owner='core',application_token=excluded.application_token",token);
        transaction.Commit();
    }

    private void ReleaseUnmigratedOwner()
    {
        using var transaction=Begin();
        if(Convert.ToInt64(Scalar("SELECT completed FROM core_queue_migration WHERE id=1")??0)==1
            || Scalar("SELECT id FROM core_queue_sessions WHERE id='active'") is not null)
            throw new InvalidOperationException("Export the latest Core session before releasing its owner.");
        RemoveSession("migration");
        RemoveSession("migration-original");
        Run("DELETE FROM core_queue_legacy_snapshot;DELETE FROM core_queue_migration");
        Run("UPDATE core_queue_owner SET owner='legacy',application_token=NULL WHERE id=1");
        transaction.Commit();
    }
}
