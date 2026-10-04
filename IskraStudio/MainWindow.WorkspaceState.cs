namespace IskraStudio;

public partial class MainWindow
{
    private enum WorkspaceSelectionMode
    {
        None,
        DeleteScenes,
        RenameScene
    }

    private enum EntryDialogMode
    {
        CreateScene,
        CreateObject,
        RenameScene,
        RenameObject
    }

    private IskraProject? activeProject;
    private IskraScene? activeScene;
    private bool showingObjects;
    private WorkspaceSelectionMode workspaceSelectionMode;
    private readonly HashSet<Guid> selectedSceneIds = [];
    private object? entryForActions;
    private IskraScene? sceneForActions;
    private EntryDialogMode entryDialogMode;
    private object? entryBeingRenamed;
    private IskraScene? sceneBeingEdited;
    private string pendingRenameName = string.Empty;
    private List<IskraScene> scenesBeingDeleted = [];
    private IskraObject? objectBeingDeleted;
    private IskraScene? objectDeletionScene;
}
