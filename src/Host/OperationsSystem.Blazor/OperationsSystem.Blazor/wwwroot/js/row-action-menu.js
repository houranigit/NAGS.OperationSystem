const observers = new WeakMap();

// Radzen positions a context menu once. Keep the same anchor while lazy content and
// nested details change its size, and bound the scrollable panel to the visible viewport.
export function observe(panel) {
  unobserve(panel);
  // Radzen positions the outer popup; the inner .rz-context-menu is only its content.
  const popup = panel?.closest(".rz-popup");
  if (!popup) return;

  let frame = 0;
  let anchorTop;
  let anchorLeft;
  let appliedTop;
  let appliedLeft;

  function fit() {
    frame = 0;
    if (!panel.isConnected) {
      unobserve(panel);
      return;
    }

    // Capture Radzen's initial placement, ignoring adjustments made by this observer.
    const top = Number.parseFloat(popup.style.top);
    const left = Number.parseFloat(popup.style.left);
    if (!Number.isFinite(top) || !Number.isFinite(left)) return;
    if (anchorTop === undefined || popup.style.top !== appliedTop) anchorTop = top;
    if (anchorLeft === undefined || popup.style.left !== appliedLeft) anchorLeft = left;

    const viewport = window.visualViewport;
    const width = viewport?.width ?? window.innerWidth;
    const height = viewport?.height ?? window.innerHeight;
    const fixed = getComputedStyle(popup).position === "fixed";
    const originTop = (fixed ? 0 : window.scrollY) + (viewport?.offsetTop ?? 0);
    const originLeft = (fixed ? 0 : window.scrollX) + (viewport?.offsetLeft ?? 0);
    const padding = 8;
    const maxHeight = `${Math.max(1, height - padding * 2)}px`;
    const maxWidth = `${Math.max(1, width - padding * 2)}px`;
    if (panel.style.maxHeight !== maxHeight) panel.style.maxHeight = maxHeight;
    if (panel.style.maxWidth !== maxWidth) panel.style.maxWidth = maxWidth;

    const bounds = popup.getBoundingClientRect();
    const fittedTop = Math.max(originTop + padding,
      Math.min(anchorTop, originTop + height - bounds.height - padding));
    const fittedLeft = Math.max(originLeft + padding,
      Math.min(anchorLeft, originLeft + width - bounds.width - padding));
    const topValue = `${fittedTop}px`;
    const leftValue = `${fittedLeft}px`;
    if (popup.style.top !== topValue) popup.style.top = topValue;
    if (popup.style.left !== leftValue) popup.style.left = leftValue;
    appliedTop = popup.style.top;
    appliedLeft = popup.style.left;
  }

  function schedule() {
    if (!frame) frame = requestAnimationFrame(fit);
  }

  const resize = new ResizeObserver(schedule);
  resize.observe(panel);
  const placement = new MutationObserver(schedule);
  placement.observe(popup, { attributes: true, attributeFilter: ["style", "class"] });
  const removal = new MutationObserver(() => {
    if (!panel.isConnected) unobserve(panel);
  });
  removal.observe(document.body, { childList: true, subtree: true });
  window.addEventListener("resize", schedule);
  window.visualViewport?.addEventListener("resize", schedule);
  window.visualViewport?.addEventListener("scroll", schedule);

  observers.set(panel, () => {
    if (frame) cancelAnimationFrame(frame);
    resize.disconnect();
    placement.disconnect();
    removal.disconnect();
    window.removeEventListener("resize", schedule);
    window.visualViewport?.removeEventListener("resize", schedule);
    window.visualViewport?.removeEventListener("scroll", schedule);
  });
  schedule();
}

export function unobserve(panel) {
  const dispose = observers.get(panel);
  if (dispose) {
    observers.delete(panel);
    dispose();
  }
}
