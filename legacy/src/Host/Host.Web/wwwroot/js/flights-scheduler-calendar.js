/**
 * Loads Vuexy app-calendar scripts once, then boots FullCalendar (SPA — no DOMContentLoaded).
 */
(function () {
  const base = '/vuexy/assets';

  function appendScript(src) {
    return new Promise((resolve, reject) => {
      const s = document.createElement('script');
      s.src = src;
      s.async = false;
      s.onload = () => resolve();
      s.onerror = () => reject(new Error('Failed to load: ' + src));
      document.body.appendChild(s);
    });
  }

  async function ensureVuexyCalendarLibs() {
    if (window.__vuexyFlightsSchedulerLibsLoaded) {
      return;
    }

    document.documentElement.setAttribute('data-assets-path', base + '/');
    document.documentElement.setAttribute('data-template', 'vertical-menu-template');
    window.assetsPath = base + '/';

    await appendScript(base + '/vendor/libs/jquery/jquery.js');
    await appendScript(base + '/vendor/libs/popper/popper.js');
    await appendScript(base + '/vendor/js/bootstrap.js');
    await appendScript(base + '/vendor/libs/node-waves/node-waves.js');
    await appendScript(base + '/vendor/libs/pickr/pickr.js');
    await appendScript(base + '/vendor/libs/perfect-scrollbar/perfect-scrollbar.js');
    await appendScript(base + '/vendor/libs/hammer/hammer.js');
    await appendScript(base + '/vendor/js/helpers.js');

    window.isRtl = window.Helpers && typeof window.Helpers.isRtl === 'function' ? window.Helpers.isRtl() : false;

    await appendScript(base + '/vendor/libs/fullcalendar.js');
    await appendScript(base + '/vendor/libs/@form-validation/popular.js');
    await appendScript(base + '/vendor/libs/@form-validation/bootstrap5.js');
    await appendScript(base + '/vendor/libs/@form-validation/auto-focus.js');
    await appendScript(base + '/vendor/libs/select2/select2.js');
    await appendScript(base + '/vendor/libs/moment/moment.js');
    await appendScript(base + '/vendor/libs/flatpickr/flatpickr.js');
    await appendScript(base + '/js/app-calendar-events.js');
    await appendScript(base + '/js/app-calendar.js');
    await appendScript('/js/ops-flights-calendar-bridge.js');

    window.__vuexyFlightsSchedulerLibsLoaded = true;
  }

  window.initFlightsSchedulerCalendar = async function initFlightsSchedulerCalendar() {
    await ensureVuexyCalendarLibs();
    if (typeof window.vuexyAppCalendarBoot === 'function') {
      window.vuexyAppCalendarBoot();
    }
  };
})();
