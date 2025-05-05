using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace TicketHub.Notifier.Email;

/// <summary>Renders Razor components to static HTML outside of any request.</summary>
public sealed class TemplateRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
{
    public async Task<string> RenderAsync<TComponent>(IDictionary<string, object?> parameters) where TComponent : IComponent
    {
        await using var renderer = new HtmlRenderer(services, loggerFactory);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
    }
}
