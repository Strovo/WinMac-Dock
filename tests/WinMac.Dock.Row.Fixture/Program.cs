using System.Drawing;
using System.Windows.Forms;

internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var context = new ApplicationContext();
        var forms = new List<Form>();
        foreach (var screen in Screen.AllScreens)
        {
            var bounds = screen.WorkingArea;
            var form = new Form { Text = "Row regression " + screen.DeviceName, StartPosition = FormStartPosition.Manual,
                Bounds = new Rectangle(bounds.Left + 120, bounds.Top + 120, 320, 160), ShowInTaskbar = true };
            form.FormClosed += (_, _) => { forms.Remove(form); if (forms.Count == 0) context.ExitThread(); };
            forms.Add(form); form.Show();
        }
        Application.Run(context);
    }
}
