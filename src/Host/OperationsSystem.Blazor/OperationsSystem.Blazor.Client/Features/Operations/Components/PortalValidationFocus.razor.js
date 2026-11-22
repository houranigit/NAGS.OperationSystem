const focusable = "input:not([type=hidden]), textarea, select, button, [tabindex]";

export function focusFirstInvalid(container) {
    if (!container?.isConnected) {
        return;
    }

    for (const field of container.querySelectorAll('[aria-invalid="true"]')) {
        const control = field.matches(focusable) ? field : field.querySelector(focusable);
        if (!control || control.closest(':disabled, [aria-disabled="true"]') ||
            !control.getClientRects().length || getComputedStyle(control).visibility !== "visible") {
            continue;
        }

        control.scrollIntoView({ block: "center", inline: "nearest" });
        control.focus({ preventScroll: true });
        return;
    }
}
