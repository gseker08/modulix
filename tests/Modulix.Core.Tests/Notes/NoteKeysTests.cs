using Modulix.Core.Contracts.Notes;
using Modulix.Core.Notes;
using Modulix.Shared.Notes;

namespace Modulix.Core.Tests.Notes;

public class NoteKeysTests
{
    [Fact]
    public void Client_keys_match_the_server()
    {
        Assert.Equal(NoteEntities.Note, NoteAccess.EntityKey);
        Assert.Equal(RecordNote.BodyMaxLength, NoteAccess.BodyMaxLength);
    }

    [Fact]
    public void Touch_raises_the_version_and_a_new_note_starts_at_one()
    {
        var note = new RecordNote { ParentEntityKey = "crm.company", ParentId = Guid.NewGuid(), Body = "Not" };
        Assert.Equal(1, note.Version);
        note.Touch();
        Assert.Equal(2, note.Version);
    }
}
