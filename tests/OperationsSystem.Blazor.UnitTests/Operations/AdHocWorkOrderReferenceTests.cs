using System.Collections;
using System.Reflection;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Features.Operations.Components;
using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Operations;

public sealed class AdHocWorkOrderReferenceTests
{
    [Theory]
    [InlineData("WP-00042", "SERVICE-001", "TASK-002")]
    [InlineData(null, null, null)]
    public void Form_request_preserves_optional_references(
        string? workPackage, string? serviceReference, string? taskReference)
    {
        var dialog = new AdHocWorkOrderDialog();
        var dialogType = typeof(AdHocWorkOrderDialog);
        dialogType.GetProperty("UserTimeZone", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(dialog, new UserTimeZone(new UnusedRuntime()));
        var model = dialogType.GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
        Set(model, "WorkPackageNumber", workPackage);
        var from = new DateTime(2026, 10, 30, 10, 0, 0);
        var to = from.AddHours(1);
        var service = NewForm(dialogType, "ServiceLineForm");
        Set(service, "ServiceId", Guid.NewGuid());
        Set(service, "FromUtc", from);
        Set(service, "ToUtc", to);
        Set(service, "CustomerReferenceNumber", serviceReference);
        ((IList)model.GetType().GetProperty("ServiceLines")!.GetValue(model)!).Add(service);
        var task = NewForm(dialogType, "TaskForm");
        Set(task, "FromUtc", from);
        Set(task, "ToUtc", to);
        Set(task, "CustomerReferenceNumber", taskReference);
        ((IList)model.GetType().GetProperty("Tasks")!.GetValue(model)!).Add(task);

        var request = (WorkOrderRequestModel)dialogType
            .GetMethod("BuildWorkOrderRequest", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dialog, null)!;

        request.WorkPackageNumber.ShouldBe(workPackage);
        request.ServiceLines.ShouldHaveSingleItem().CustomerReferenceNumber.ShouldBe(serviceReference);
        request.Tasks.ShouldHaveSingleItem().CustomerReferenceNumber.ShouldBe(taskReference);
    }

    private static object NewForm(Type dialogType, string name) =>
        Activator.CreateInstance(dialogType.GetNestedType(name, BindingFlags.NonPublic)!, nonPublic: true)!;

    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property)!.SetValue(target, value);

    private sealed class UnusedRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException("Request mapping must not invoke JavaScript.");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new InvalidOperationException("Request mapping must not invoke JavaScript.");
    }
}
