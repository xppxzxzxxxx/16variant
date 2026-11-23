using System.Windows;
using System.Windows.Input;

namespace DrivingSchool.Desktop;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        SignInButton.CommandParameter = (Func<string>)(() => Password.Password);
        Loaded += (_, _) => Keyboard.Focus(Login);
    }
}
