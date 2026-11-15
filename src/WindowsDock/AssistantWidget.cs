namespace WindowsDock;

public partial class MainWindow
{
    private DashboardWindow? assistant;

    private void AddAssistantWidget() => AddButton("Assistant", "✦", OpenAssistant);

    private void OpenAssistant()
    {
        if (assistant == null)
        {
            assistant = new DashboardWindow(0, null, deferPage: true) { Owner = this, ShowInTaskbar = false };
            assistant.Closed += (_, _) => assistant = null;
        }
        assistant.ShowDockPanelHost();
        assistant.ShowAssistantPicker();
    }
}
