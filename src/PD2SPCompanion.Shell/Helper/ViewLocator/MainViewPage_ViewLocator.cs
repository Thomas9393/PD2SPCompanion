using Avalonia.Controls;
using Avalonia.Controls.Templates;
using PD2SPCompanion.Shell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace PD2SPCompanion.Shell.Helper.ViewLocator;

/// <summary>
/// The view for a page's view model: the one of the same name, built once by the services (App.Services).
/// <para>
/// A view can be the view of only one place at a time. The vault list is shown on either page of
/// the Items book, and while one page fades out as
/// the other fades in both hold it - so the right page's locator asks for the view registered
/// under its <paramref name="side"/> first, a second instance over the same view model.
/// </para>
/// </summary>
public class MainViewPage_ViewLocator : IDataTemplate
{
    private readonly IServiceProvider services;
    private readonly string? side;

    public MainViewPage_ViewLocator(IServiceProvider services) : this(services, null) { }

    public MainViewPage_ViewLocator(IServiceProvider services, string? side)
    {
        this.services = services;
        this.side = side;
    }

    /// <summary>What the right page's views are registered under, where it has its own.</summary>
    public const string RightSide = "right";

    public Control Build(object? data)
    {
       if (data is null)
        {
            return new TextBlock { Text = L("Missing data.") };
        }

        var name = data.GetType().FullName!.Replace("ViewModel", "View");
        var type = Type.GetType(name);

        if (type != null)
        {
            if (side is not null && services is IKeyedServiceProvider keyed
                && keyed.GetKeyedService(type, side) is Control own)
            {
                return own;
            }

            return (Control)services.GetRequiredService(type);
        }
        else
            return new TextBlock { Text = name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}