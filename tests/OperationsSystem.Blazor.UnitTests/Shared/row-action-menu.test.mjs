import assert from "node:assert/strict";
import { test } from "node:test";
import { observe, unobserve } from "../../../src/Host/OperationsSystem.Blazor/OperationsSystem.Blazor/wwwroot/js/row-action-menu.js";

function environment({ top = 500, left = 780, width = 220, height = 80, scrollY = 0, viewportWidth = 1000, viewportHeight = 600 } = {}) {
  const frames = new Map();
  const resizeObservers = [];
  const mutationObservers = [];
  const events = new Map();
  let frameId = 0;
  const window = {
    innerWidth: viewportWidth, innerHeight: viewportHeight, scrollX: 0, scrollY,
    addEventListener(name, callback) { events.set(name, callback); },
    removeEventListener(name) { events.delete(name); }
  };
  const panel = {
    className: "os-row-actions-menu", style: {}, isConnected: true, width, height,
    closest(selector) {
      for (let element = this; element; element = element.parentElement) {
        if (element.className?.split(" ").includes(selector.slice(1))) return element;
      }
      return null;
    }
  };
  const contextMenu = { className: "rz-context-menu", parentElement: null };
  const popup = {
    className: "rz-tooltip rz-popup rz-open",
    style: { top: `${top}px`, left: `${left}px` },
    getBoundingClientRect() {
      return {
        width: Math.min(panel.width, Number.parseFloat(panel.style.maxWidth) || Infinity),
        height: Math.min(panel.height, Number.parseFloat(panel.style.maxHeight) || Infinity)
      };
    }
  };
  panel.parentElement = contextMenu;
  contextMenu.parentElement = popup;
  globalThis.window = window;
  globalThis.document = { body: {} };
  globalThis.getComputedStyle = () => ({ position: "absolute" });
  globalThis.requestAnimationFrame = callback => { frames.set(++frameId, callback); return frameId; };
  globalThis.cancelAnimationFrame = id => frames.delete(id);
  class Observer {
    constructor(callback) { this.callback = callback; this.disconnected = false; }
    observe(target) { this.target = target; }
    disconnect() { this.disconnected = true; }
  }
  globalThis.ResizeObserver = class extends Observer {
    constructor(callback) { super(callback); resizeObservers.push(this); }
  };
  globalThis.MutationObserver = class extends Observer {
    constructor(callback) { super(callback); mutationObservers.push(this); }
  };
  function flush() {
    for (const [id, callback] of frames) { frames.delete(id); callback(); }
  }
  return { panel, contextMenu, popup, window, events, resizeObservers, mutationObservers, frames, flush };
}

test("the rendered Radzen wrapper adjusts the positioned popup with an eight pixel viewport gap", () => {
  const state = environment({ top: 298, left: 1048.32, width: 231.68, height: 508, viewportWidth: 1280, viewportHeight: 720 });
  observe(state.panel);
  state.flush();
  assert.equal(state.popup.style.top, "204px");
  assert.equal(state.popup.style.left, "1040.32px");
  assert.equal(state.mutationObservers[0].target, state.popup);
  assert.deepEqual(state.contextMenu.style, undefined);
  unobserve(state.panel);
});

test("a menu stays within the viewport as lazy submenu content grows and collapses", () => {
  const state = environment();
  observe(state.panel);
  state.flush();
  assert.equal(state.popup.style.top, "500px");
  assert.equal(state.popup.style.left, "772px");

  state.panel.height = 450;
  state.resizeObservers[0].callback();
  state.flush();
  assert.equal(state.popup.style.top, "142px");

  state.panel.height = 80;
  state.resizeObservers[0].callback();
  state.flush();
  assert.equal(state.popup.style.top, "500px");
  unobserve(state.panel);
});

test("oversized content scrolls within the visible viewport including document scroll offsets", () => {
  const state = environment({ top: 750, left: 780, width: 1400, height: 1100, scrollY: 200 });
  observe(state.panel);
  state.flush();
  assert.equal(state.panel.style.maxHeight, "584px");
  assert.equal(state.panel.style.maxWidth, "984px");
  assert.equal(state.popup.style.top, "208px");
  assert.equal(state.popup.style.left, "8px");
  unobserve(state.panel);
});

test("Radzen changing the initial position replaces the anchor", () => {
  const state = environment();
  observe(state.panel);
  state.flush();
  state.popup.style.top = "100px";
  state.popup.style.left = "300px";
  state.mutationObservers[0].callback();
  state.flush();
  state.panel.height = 200;
  state.resizeObservers[0].callback();
  state.flush();
  assert.equal(state.popup.style.top, "100px");
  assert.equal(state.popup.style.left, "300px");
  unobserve(state.panel);
});

test("removing the popup releases all observers listeners and pending frames", () => {
  const state = environment();
  observe(state.panel);
  state.panel.isConnected = false;
  state.mutationObservers[1].callback();
  assert.equal(state.frames.size, 0);
  assert.equal(state.events.size, 0);
  assert.ok(state.resizeObservers.every(observer => observer.disconnected));
  assert.ok(state.mutationObservers.every(observer => observer.disconnected));
});
