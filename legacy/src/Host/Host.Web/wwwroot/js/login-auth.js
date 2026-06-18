'use strict';

/** @param {string} text */
window.copyTextToClipboard = function (text) {
  return navigator.clipboard.writeText(text);
};

/**
 * Password visibility toggle for login / activate (Vuexy-style markup).
 */
window.loginAuth = {
  init: function () {
    document.querySelectorAll('[data-auth-password-toggle]').forEach(function (host) {
      if (host.dataset.authWired === '1') return;
      host.dataset.authWired = '1';

      var inputId = host.getAttribute('data-auth-password-toggle');
      var input = inputId ? document.getElementById(inputId) : null;
      if (!input) return;

      var eye = host.querySelector('.auth-eye');
      var eyeOff = host.querySelector('.auth-eye-off');

      function syncIcons() {
        var isPwd = input.getAttribute('type') === 'password';
        if (eye) eye.classList.toggle('d-none', isPwd);
        if (eyeOff) eyeOff.classList.toggle('d-none', !isPwd);
      }

      host.addEventListener('click', function (e) {
        e.preventDefault();
        var next = input.getAttribute('type') === 'password' ? 'text' : 'password';
        input.setAttribute('type', next);
        host.setAttribute('aria-pressed', next === 'text' ? 'true' : 'false');
        host.setAttribute('aria-label', next === 'text' ? 'Hide password' : 'Show password');
        syncIcons();
      });

      host.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          host.click();
        }
      });

      syncIcons();
    });
  }
};
