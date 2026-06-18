/* Blazor interop: flights calendar (Vuexy app-calendar.js) calls into .NET for events + empty-cell click. */
(function () {
  'use strict';

  window._opsFlightsCalDotNet = null;
  /** @type {import('@fullcalendar/core').Calendar | null} */
  window._opsFlightsCalCal = null;

  window.opsRegisterFlightsCalendar = function (dotNetRef) {
    window._opsFlightsCalDotNet = dotNetRef;
    if (window._opsFlightsCalCal) {
      try {
        window._opsFlightsCalCal.refetchEvents();
      } catch (e) {
        /* ignore */
      }
    }
  };

  window.opsUnregisterFlightsCalendar = function () {
    window._opsFlightsCalDotNet = null;
    window._opsFlightsCalCal = null;
  };

  window.opsFlightsCalRefetch = function () {
    try {
      if (window._opsFlightsCalCal) window._opsFlightsCalCal.refetchEvents();
    } catch (e) {
      /* ignore */
    }
  };

  window.opsFlightsCalendarSetLoading = function (on) {
    var el = document.querySelector('.app-calendar-content');
    if (!el) return;
    el.classList.toggle('ops-fc-loading', !!on);
    el.setAttribute('aria-busy', on ? 'true' : 'false');
  };
})();
