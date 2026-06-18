/**
 * App Calendar
 */

/**
 * ! If both start and end dates are same Full calendar will nullify the end date value.
 * ! Full calendar will end the event on a day before at 12:00:00AM thus, event won't extend to the end date.
 * ! We are getting events from a separate file named app-calendar-events.js. You can add or remove events from there.
 *
 **/

'use strict';

window.vuexyAppCalendarBoot = function vuexyAppCalendarBoot() {
  const calendarElProbe = document.getElementById('calendar');
  if (!calendarElProbe || calendarElProbe.classList.contains('fc')) {
    return;
  }
  const calendarEl = calendarElProbe;
  const direction = typeof isRtl !== 'undefined' && isRtl ? 'rtl' : 'ltr';
  (function () {
    // DOM Elements
    const appCalendarSidebar = document.querySelector('.app-calendar-sidebar');
    const addEventSidebar = document.getElementById('addEventSidebar');
    const appOverlay = document.querySelector('.app-overlay');
    const offcanvasTitle = document.querySelector('.offcanvas-title');
    const btnToggleSidebar = document.querySelector('.btn-toggle-sidebar');
    const btnSubmit = document.getElementById('addEventBtn');
    const btnDeleteEvent = document.querySelector('.btn-delete-event');
    const btnCancel = document.querySelector('.btn-cancel');
    const eventTitle = document.getElementById('eventTitle');
    const eventStartDate = document.getElementById('eventStartDate');
    const eventEndDate = document.getElementById('eventEndDate');
    const eventUrl = document.getElementById('eventURL');
    const eventLocation = document.getElementById('eventLocation');
    const eventDescription = document.getElementById('eventDescription');
    const allDaySwitch = document.querySelector('.allDay-switch'); // optional; flights page omits it
    const selectAll = document.querySelector('.select-all');
    const filterInputs = Array.from(document.querySelectorAll('.input-filter'));
    const inlineCalendar = document.querySelector('.inline-calendar');

    // Calendar settings
    const calendarColors = {
      Business: 'primary',
      Holiday: 'success',
      Personal: 'danger',
      Family: 'warning',
      ETC: 'info'
    };

    // External jQuery Elements
    const eventLabel = $('#eventLabel'); // ! Using jQuery vars due to select2 jQuery dependency
    const eventGuests = $('#eventGuests'); // ! Using jQuery vars due to select2 jQuery dependency

    // Event Data
    let currentEvents = events; // Assuming events are imported from app-calendar-events.js
    let isFormValid = false;
    let eventToUpdate = null;
    let inlineCalInstance = null;

    // Offcanvas Instance
    const bsAddEventSidebar = addEventSidebar ? new bootstrap.Offcanvas(addEventSidebar) : null;

    //! TODO: Update Event label and guest code to JS once select removes jQuery dependency
    // Initialize Select2 with custom templates
    if (eventLabel.length) {
      function renderBadges(option) {
        if (!option.id) {
          return option.text;
        }
        var $badge =
          "<span class='badge badge-dot bg-" + $(option.element).data('label') + " me-2'> " + '</span>' + option.text;

        return $badge;
      }
      eventLabel.wrap('<div class="position-relative"></div>').select2({
        placeholder: 'Select value',
        dropdownParent: eventLabel.parent(),
        templateResult: renderBadges,
        templateSelection: renderBadges,
        minimumResultsForSearch: -1,
        escapeMarkup: function (es) {
          return es;
        }
      });
    }

    // Render guest avatars
    if (eventGuests.length) {
      function renderGuestAvatar(option) {
        if (!option.id) return option.text;
        return `
    <div class='d-flex flex-wrap align-items-center'>
      <div class='avatar avatar-xs me-2'>
        <img src='${assetsPath}img/avatars/${$(option.element).data('avatar')}'
          alt='avatar' class='rounded-circle' />
      </div>
      ${option.text}
    </div>`;
      }
      eventGuests.wrap('<div class="position-relative"></div>').select2({
        placeholder: 'Select value',
        dropdownParent: eventGuests.parent(),
        closeOnSelect: false,
        templateResult: renderGuestAvatar,
        templateSelection: renderGuestAvatar,
        escapeMarkup: function (es) {
          return es;
        }
      });
    }

    // Event start (flatpicker)
    if (eventStartDate) {
      var start = eventStartDate.flatpickr({
        monthSelectorType: 'static',
        static: true,
        enableTime: true,
        altFormat: 'Y-m-dTH:i:S',
        onReady: function (selectedDates, dateStr, instance) {
          if (instance.isMobile) {
            instance.mobileInput.setAttribute('step', null);
          }
        }
      });
    }

    // Event end (flatpicker)
    if (eventEndDate) {
      var end = eventEndDate.flatpickr({
        monthSelectorType: 'static',
        static: true,
        enableTime: true,
        altFormat: 'Y-m-dTH:i:S',
        onReady: function (selectedDates, dateStr, instance) {
          if (instance.isMobile) {
            instance.mobileInput.setAttribute('step', null);
          }
        }
      });
    }

    // Inline sidebar calendar (flatpicker) — default to today (same as main month grid)
    if (inlineCalendar) {
      inlineCalInstance = inlineCalendar.flatpickr({
        monthSelectorType: 'static',
        static: true,
        inline: true,
        defaultDate: new Date()
      });
    }

    // Event click function
    function eventClick(info) {
      eventToUpdate = info.event;
      if (eventToUpdate.url) {
        info.jsEvent.preventDefault();
        window.open(eventToUpdate.url, '_blank');
      }
      if (bsAddEventSidebar) bsAddEventSidebar.show();
      // For update event set offcanvas title text: Update Event
      if (offcanvasTitle) {
        offcanvasTitle.innerHTML = 'Update Event';
      }
      btnSubmit.innerHTML = 'Update';
      btnSubmit.classList.add('btn-update-event');
      btnSubmit.classList.remove('btn-add-event');
      btnDeleteEvent.classList.remove('d-none');

      eventTitle.value = eventToUpdate.title;
      start.setDate(eventToUpdate.start, true, 'Y-m-d');
      if (allDaySwitch) {
        eventToUpdate.allDay === true ? (allDaySwitch.checked = true) : (allDaySwitch.checked = false);
      }
      eventToUpdate.end !== null
        ? end.setDate(eventToUpdate.end, true, 'Y-m-d')
        : end.setDate(eventToUpdate.start, true, 'Y-m-d');
      eventLabel.val(eventToUpdate.extendedProps.calendar).trigger('change');
      eventToUpdate.extendedProps.location !== undefined
        ? (eventLocation.value = eventToUpdate.extendedProps.location)
        : null;
      eventToUpdate.extendedProps.guests !== undefined
        ? eventGuests.val(eventToUpdate.extendedProps.guests).trigger('change')
        : null;
      eventToUpdate.extendedProps.description !== undefined
        ? (eventDescription.value = eventToUpdate.extendedProps.description)
        : null;
    }

    // Modify sidebar toggler
    function modifyToggler() {
      const fcSidebarToggleButton = document.querySelector('.fc-sidebarToggle-button');
      if (!fcSidebarToggleButton) {
        return;
      }
      fcSidebarToggleButton.classList.remove('fc-button-primary');
      fcSidebarToggleButton.classList.add('d-lg-none', 'd-inline-block', 'ps-0');
      while (fcSidebarToggleButton.firstChild) {
        fcSidebarToggleButton.firstChild.remove();
      }
      fcSidebarToggleButton.setAttribute('data-bs-toggle', 'sidebar');
      fcSidebarToggleButton.setAttribute('data-overlay', '');
      fcSidebarToggleButton.setAttribute('data-target', '#app-calendar-sidebar');
      fcSidebarToggleButton.insertAdjacentHTML(
        'beforeend',
        '<i class="icon-base ti tabler-menu-2 icon-lg text-heading"></i>'
      );
    }

    // Filter events by calender (demo template)
    function selectedCalendars() {
      let selected = [],
        filterInputChecked = [].slice.call(document.querySelectorAll('.input-filter:checked'));

      filterInputChecked.forEach(item => {
        selected.push(item.getAttribute('data-value'));
      });

      return selected;
    }

    /**
     * Flights: status checkboxes. '' = none; '*' = all; else comma enum names.
     */
    function selectedFlightStatusParam() {
      var all = [].slice.call(document.querySelectorAll('.input-filter'));
      var checked = [].slice.call(document.querySelectorAll('.input-filter:checked'));
      if (all.length === 0) {
        return '*';
      }
      if (checked.length === 0) {
        return '';
      }
      if (checked.length === all.length) {
        return '*';
      }
      return checked
        .map(function (c) {
          return c.getAttribute('data-value') || '';
        })
        .filter(Boolean)
        .join(',');
    }

    // --------------------------------------------------------------------------------------------------
    // fetchEvents: flights → Blazor + query; else demo data from app-calendar-events.js
    // --------------------------------------------------------------------------------------------------
    function fetchEvents(info, successCallback, failureCallback) {
      if (window._opsFlightsCalDotNet) {
        var startD =
          info && info.start ? (info.start instanceof Date ? info.start : new Date(info.start)) : new Date();
        var endD =
          info && info.end ? (info.end instanceof Date ? info.end : new Date(info.end)) : startD;
        var fromIso = startD.toISOString();
        var toIso = endD.toISOString();
        var statusParam = selectedFlightStatusParam();
        if (typeof window.opsFlightsCalendarSetLoading === 'function') {
          window.opsFlightsCalendarSetLoading(true);
        }
        window._opsFlightsCalDotNet
          .invokeMethodAsync('GetFlightEvents', fromIso, toIso, statusParam)
          .then(function (ev) {
            successCallback(ev || []);
          })
          .catch(function (err) {
            if (typeof failureCallback === 'function') {
              failureCallback(err);
            } else {
              successCallback([]);
            }
          })
          .finally(function () {
            if (typeof window.opsFlightsCalendarSetLoading === 'function') {
              window.opsFlightsCalendarSetLoading(false);
            }
          });
        return;
      }
      let calendars = selectedCalendars();
      let selectedEvents = currentEvents.filter(function (event) {
        return calendars.includes(event.extendedProps.calendar.toLowerCase());
      });
      successCallback(selectedEvents);
    }

    // Init FullCalendar
    // ------------------------------------------------
    let calendar = new Calendar(calendarEl, {
      initialView: 'dayGridMonth',
      events: fetchEvents,
      plugins: [dayGridPlugin, interactionPlugin, listPlugin, timegridPlugin],
      editable: true,
      dragScroll: true,
      dayMaxEvents: 2,
      eventResizableFromStart: true,
      customButtons: {
        sidebarToggle: {
          text: 'Sidebar'
        }
      },
      headerToolbar: {
        start: 'sidebarToggle, prev,next, title',
        end: 'dayGridMonth,timeGridWeek,timeGridDay,listMonth'
      },
      direction: direction,
      initialDate: new Date(),
      navLinks: true, // can click day/week names to navigate views
      // timeGridWeek/Day: full 0–24h, no all-day row; contentHeight auto lays out every slot (no clipped 6am–9pm window)
      views: {
        timeGrid: {
          allDaySlot: false,
          slotMinTime: '00:00:00',
          slotMaxTime: '24:00:00',
          scrollTime: '00:00:00',
          contentHeight: 'auto',
          expandRows: false
        }
      },
      eventClassNames: function ({ event: calendarEvent }) {
        if (window._opsFlightsCalDotNet) {
          return [];
        }
        const colorName = calendarColors[calendarEvent._def.extendedProps.calendar];
        // Background Color
        return ['bg-label-' + colorName];
      },
      dateClick: function (info) {
        if (window._opsFlightsCalDotNet) {
          var dateStr = info.dateStr;
          if (!dateStr && info.date) {
            try {
              dateStr = info.date.toISOString();
            } catch (e) {
              dateStr = moment(info.date).toISOString();
            }
          }
          window._opsFlightsCalDotNet.invokeMethodAsync('OnCalendarDateClicked', dateStr);
          return;
        }
        let date = moment(info.date).format('YYYY-MM-DD');
        resetValues();
        if (bsAddEventSidebar) bsAddEventSidebar.show();

        // For new event set offcanvas title text: Add Event
        if (offcanvasTitle) {
          offcanvasTitle.innerHTML = 'Add Event';
        }
        btnSubmit.innerHTML = 'Add';
        btnSubmit.classList.remove('btn-update-event');
        btnSubmit.classList.add('btn-add-event');
        btnDeleteEvent.classList.add('d-none');
        eventStartDate.value = date;
        eventEndDate.value = date;
      },
      eventClick: function (info) {
        if (window._opsFlightsCalDotNet) {
          info.jsEvent.preventDefault();
          var eid = info.event && info.event.id;
          if (eid) {
            window._opsFlightsCalDotNet.invokeMethodAsync('OnFlightCalendarEventClicked', String(eid));
          }
          return;
        }
        eventClick(info);
      },
      eventDidMount: function (info) {
        if (!window._opsFlightsCalDotNet) {
          return;
        }
        var ev = info.event;
        var root = info.el;
        if (!root || !ev) {
          return;
        }
        var bg = ev.backgroundColor;
        var bc = ev.borderColor;
        var tc = ev.textColor;
        if (bg) {
          root.style.backgroundColor = bg;
        }
        if (bc) {
          root.style.borderColor = bc;
        }
        if (tc) {
          root.style.color = tc;
        }
        var main = root.querySelector('.fc-event-main');
        if (main) {
          if (tc) {
            main.style.color = tc;
          }
          main.style.backgroundColor = 'transparent';
        }
        // Flights: render "{CustomerCode}{FlightNo} {h:mma}" in the cell and a fuller
        // "{...} on {StationName}" native tooltip on hover. Time comes from event.start
        // so it always matches FullCalendar's local-time placement.
        var ep = ev.extendedProps || {};
        var code = (ep.customerCode || '').toString();
        var fn = (ep.flightNumber || ev.title || '').toString();
        var station = (ep.stationName || '').toString();
        var startDate = ev.start instanceof Date ? ev.start : (ev.start ? new Date(ev.start) : null);
        var timeStr = '';
        if (startDate && !isNaN(startDate.getTime())) {
          var h = startDate.getHours();
          var m = startDate.getMinutes();
          var ampm = h >= 12 ? 'PM' : 'AM';
          var h12 = h % 12;
          if (h12 === 0) h12 = 12;
          var mm = m < 10 ? '0' + m : '' + m;
          timeStr = h12 + ':' + mm + ' ' + ampm;
        }
        var label = (code + fn).trim();
        if (timeStr) {
          label = label ? (label + ' ' + timeStr) : timeStr;
        }
        var titleEls = root.querySelectorAll('.fc-event-title');
        for (var i = 0; i < titleEls.length; i++) {
          titleEls[i].textContent = label;
        }
        var timeEls = root.querySelectorAll('.fc-event-time');
        for (var j = 0; j < timeEls.length; j++) {
          // Time is already part of our label in every view; hide the default time slot.
          timeEls[j].textContent = '';
          timeEls[j].style.display = 'none';
        }
        var tooltip = label;
        if (station) {
          tooltip = (tooltip ? tooltip + ' ' : '') + 'on ' + station;
        }
        if (tooltip) {
          root.setAttribute('title', tooltip);
        }
      },
      datesSet: function () {
        modifyToggler();
        if (window._opsFlightsCalDotNet && inlineCalInstance && calendar) {
          try {
            inlineCalInstance.setDate(calendar.getDate(), false);
          } catch (e) {
            /* ignore */
          }
        }
        // Flights: FullCalendar re-invokes events(fetchEvents) when the range/view changes; also refetch after
        // Blazor registration (opsRegisterFlightsCalendar) for first paint. Status filters use refetchEvents().
      },
      viewDidMount: function () {
        modifyToggler();
      }
    });

    // Render calendar (always keep ref; Blazor may register interop after this runs)
    calendar.render();
    window._opsFlightsCalCal = calendar;
    // Modify sidebar toggler
    modifyToggler();

    const eventForm = document.getElementById('eventForm');
    if (eventForm) {
      FormValidation.formValidation(eventForm, {
      fields: {
        eventTitle: {
          validators: {
            notEmpty: {
              message: 'Please enter event title '
            }
          }
        },
        eventStartDate: {
          validators: {
            notEmpty: {
              message: 'Please enter start date '
            }
          }
        },
        eventEndDate: {
          validators: {
            notEmpty: {
              message: 'Please enter end date '
            }
          }
        }
      },
      plugins: {
        trigger: new FormValidation.plugins.Trigger(),
        bootstrap5: new FormValidation.plugins.Bootstrap5({
          // Use this for enabling/changing valid/invalid class
          eleValidClass: '',
          rowSelector: function (field, ele) {
            // field is the field name & ele is the field element
            return '.form-control-validation';
          }
        }),
        submitButton: new FormValidation.plugins.SubmitButton(),
        // Submit the form when all fields are valid
        // defaultSubmit: new FormValidation.plugins.DefaultSubmit(),
        autoFocus: new FormValidation.plugins.AutoFocus()
      }
    })
      .on('core.form.valid', function () {
        // Jump to the next step when all fields in the current step are valid
        isFormValid = true;
      })
      .on('core.form.invalid', function () {
        // if fields are invalid
        isFormValid = false;
      });
    }

    // Sidebar Toggle Btn
    if (btnToggleSidebar) {
      btnToggleSidebar.addEventListener('click', e => {
        btnCancel.classList.remove('d-none');
      });
    }

    // Add Event
    // ------------------------------------------------
    function addEvent(eventData) {
      // ? Add new event data to current events object and refetch it to display on calender
      // ? You can write below code to AJAX call success response

      currentEvents.push(eventData);
      calendar.refetchEvents();

      // ? To add event directly to calender (won't update currentEvents object)
      // calendar.addEvent(eventData);
    }

    // Update Event
    // ------------------------------------------------
    function updateEvent(eventData) {
      // ? Update existing event data to current events object and refetch it to display on calender
      // ? You can write below code to AJAX call success response
      eventData.id = parseInt(eventData.id);
      currentEvents[currentEvents.findIndex(el => el.id === eventData.id)] = eventData; // Update event by id
      calendar.refetchEvents();

      // ? To update event directly to calender (won't update currentEvents object)
      // let propsToUpdate = ['id', 'title', 'url'];
      // let extendedPropsToUpdate = ['calendar', 'guests', 'location', 'description'];

      // updateEventInCalendar(eventData, propsToUpdate, extendedPropsToUpdate);
    }

    // Remove Event
    // ------------------------------------------------

    function removeEvent(eventId) {
      // ? Delete existing event data to current events object and refetch it to display on calender
      // ? You can write below code to AJAX call success response
      currentEvents = currentEvents.filter(function (event) {
        return event.id != eventId;
      });
      calendar.refetchEvents();

      // ? To delete event directly to calender (won't update currentEvents object)
      // removeEventInCalendar(eventId);
    }

    // (Update Event In Calendar (UI Only)
    // ------------------------------------------------
    const updateEventInCalendar = (updatedEventData, propsToUpdate, extendedPropsToUpdate) => {
      const existingEvent = calendar.getEventById(updatedEventData.id);

      // --- Set event properties except date related ----- //
      // ? Docs: https://fullcalendar.io/docs/Event-setProp
      // dateRelatedProps => ['start', 'end', 'allDay']
      // eslint-disable-next-line no-plusplus
      for (var index = 0; index < propsToUpdate.length; index++) {
        var propName = propsToUpdate[index];
        existingEvent.setProp(propName, updatedEventData[propName]);
      }

      // --- Set date related props ----- //
      // ? Docs: https://fullcalendar.io/docs/Event-setDates
      existingEvent.setDates(updatedEventData.start, updatedEventData.end, {
        allDay: updatedEventData.allDay
      });

      // --- Set event's extendedProps ----- //
      // ? Docs: https://fullcalendar.io/docs/Event-setExtendedProp
      // eslint-disable-next-line no-plusplus
      for (var index = 0; index < extendedPropsToUpdate.length; index++) {
        var propName = extendedPropsToUpdate[index];
        existingEvent.setExtendedProp(propName, updatedEventData.extendedProps[propName]);
      }
    };

    // Remove Event In Calendar (UI Only)
    // ------------------------------------------------
    function removeEventInCalendar(eventId) {
      calendar.getEventById(eventId).remove();
    }

    // Add new event
    // ------------------------------------------------
    if (btnSubmit) {
      btnSubmit.addEventListener('click', e => {
      if (btnSubmit.classList.contains('btn-add-event')) {
        if (isFormValid) {
          let newEvent = {
            id: calendar.getEvents().length + 1,
            title: eventTitle.value,
            start: eventStartDate.value,
            end: eventEndDate.value,
            startStr: eventStartDate.value,
            endStr: eventEndDate.value,
            display: 'block',
            extendedProps: {
              location: eventLocation.value,
              guests: eventGuests.val(),
              calendar: eventLabel.val(),
              description: eventDescription.value
            }
          };
          if (eventUrl.value) {
            newEvent.url = eventUrl.value;
          }
          if (allDaySwitch && allDaySwitch.checked) {
            newEvent.allDay = true;
          }
          addEvent(newEvent);
          if (bsAddEventSidebar) bsAddEventSidebar.hide();
        }
      } else {
        // Update event
        // ------------------------------------------------
        if (isFormValid) {
          let eventData = {
            id: eventToUpdate.id,
            title: eventTitle.value,
            start: eventStartDate.value,
            end: eventEndDate.value,
            url: eventUrl.value,
            extendedProps: {
              location: eventLocation.value,
              guests: eventGuests.val(),
              calendar: eventLabel.val(),
              description: eventDescription.value
            },
            display: 'block',
            allDay: allDaySwitch && allDaySwitch.checked ? true : false
          };

          updateEvent(eventData);
          if (bsAddEventSidebar) bsAddEventSidebar.hide();
        }
      }
    });
    }

    // Call removeEvent function
    if (btnDeleteEvent) {
      btnDeleteEvent.addEventListener('click', e => {
        removeEvent(parseInt(eventToUpdate.id));
        // eventToUpdate.remove();
        if (bsAddEventSidebar) bsAddEventSidebar.hide();
      });
    }

    // Reset event form inputs values
    // ------------------------------------------------
    function resetValues() {
      eventEndDate.value = '';
      eventUrl.value = '';
      eventStartDate.value = '';
      eventTitle.value = '';
      eventLocation.value = '';
      if (allDaySwitch) allDaySwitch.checked = false;
      eventGuests.val('').trigger('change');
      eventDescription.value = '';
    }

    // When modal hides reset input values
    if (addEventSidebar) {
      addEventSidebar.addEventListener('hidden.bs.offcanvas', function () {
        resetValues();
      });
    }

    // Hide left sidebar if the right sidebar is open
    if (btnToggleSidebar) {
      btnToggleSidebar.addEventListener('click', e => {
      if (offcanvasTitle) {
        offcanvasTitle.innerHTML = 'Add Event';
      }
      if (btnSubmit) {
        btnSubmit.innerHTML = 'Add';
        btnSubmit.classList.remove('btn-update-event');
        btnSubmit.classList.add('btn-add-event');
      }
      if (btnDeleteEvent) btnDeleteEvent.classList.add('d-none');
      appCalendarSidebar.classList.remove('show');
      appOverlay.classList.remove('show');
    });
    }
    // ------------------------------------------------
    if (selectAll) {
      selectAll.addEventListener('click', e => {
        if (e.currentTarget.checked) {
          document.querySelectorAll('.input-filter').forEach(c => (c.checked = 1));
        } else {
          document.querySelectorAll('.input-filter').forEach(c => (c.checked = 0));
        }
        calendar.refetchEvents();
      });
    }

    if (filterInputs) {
      filterInputs.forEach(item => {
        item.addEventListener('click', () => {
          document.querySelectorAll('.input-filter:checked').length < document.querySelectorAll('.input-filter').length
            ? (selectAll.checked = false)
            : (selectAll.checked = true);
          calendar.refetchEvents();
        });
      });
    }

    // Jump to date on sidebar(inline) calendar change
    if (inlineCalInstance) {
      if (!inlineCalInstance.config.onChange) {
        inlineCalInstance.config.onChange = [];
      }
      inlineCalInstance.config.onChange.push(function (date) {
      calendar.changeView(calendar.view.type, moment(date[0]).format('YYYY-MM-DD'));
      modifyToggler();
      appCalendarSidebar.classList.remove('show');
      appOverlay.classList.remove('show');
      if (window._opsFlightsCalDotNet) {
        try {
          calendar.refetchEvents();
        } catch (e) {
          /* ignore */
        }
      }
      });
    }
  })();
  if (window.Helpers && typeof window.Helpers.initSidebarToggle === 'function') {
    window.Helpers.initSidebarToggle();
  }
};
