/* TaskbarFold site: theme lock and scroll reveals. No dependencies.
   Language is a real page (/ and /en/), not a runtime switch. */
(function () {
  'use strict';

  var root = document.documentElement;
  var en = root.lang === 'en';

  /* ---- Theme bootstrap ------------------------------------------------- */
  var saved = null;
  try { saved = localStorage.getItem('tbf-theme'); } catch (e) {}
  var prefersDark = !window.matchMedia('(prefers-color-scheme: light)').matches;
  root.dataset.theme = (saved ? saved === 'dark' : prefersDark) ? 'dark' : 'light';

  /* ---- Safe screenshot fallback --------------------------------------- */
  function replaceFailedScreenshot(img) {
    var box = document.createElement('div');
    var icon = document.createElement('i');
    var title = document.createElement('b');
    var size = document.createElement('span');
    box.className = 'shot__pending';
    icon.className = 'ph ph-image';
    title.textContent = img.dataset.slot || '';
    size.textContent = img.dataset.size || '';
    box.appendChild(icon);
    box.appendChild(title);
    box.appendChild(size);
    if (img.parentNode) img.parentNode.replaceChild(box, img);
  }

  var screenshots = document.querySelectorAll('img[data-slot]');
  for (var shot = 0; shot < screenshots.length; shot++) {
    screenshots[shot].addEventListener('error', function () {
      replaceFailedScreenshot(this);
    });
  }

  /* ---- Theme ----------------------------------------------------------- */
  var themeBtn = document.getElementById('theme');

  function paintTheme() {
    var dark = root.dataset.theme === 'dark';
    var icon = themeBtn.querySelector('i');
    if (icon) icon.className = 'ph ' + (dark ? 'ph-sun' : 'ph-moon');
    themeBtn.setAttribute('aria-label', en
      ? (dark ? 'Switch to light theme' : 'Switch to dark theme')
      : (dark ? 'Включить светлую тему' : 'Включить тёмную тему'));
  }

  themeBtn.addEventListener('click', function () {
    root.dataset.theme = root.dataset.theme === 'dark' ? 'light' : 'dark';
    try { localStorage.setItem('tbf-theme', root.dataset.theme); } catch (e) {}
    paintTheme();
  });

  paintTheme();

  /* ---- Scroll reveal ---------------------------------------------------- */
  /* Entry follows reading order, so the eye lands on the first item first. */
  var reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  var targets = document.querySelectorAll('.reveal');
  var i;

  if (reduced || !('IntersectionObserver' in window)) {
    for (i = 0; i < targets.length; i++) targets[i].classList.add('in');
    return;
  }

  var io = new IntersectionObserver(function (entries) {
    entries.forEach(function (entry, n) {
      if (!entry.isIntersecting) return;
      var el = entry.target;
      el.style.transitionDelay = Math.min(n, 5) * 60 + 'ms';
      el.classList.add('in');
      io.unobserve(el);
    });
  }, { rootMargin: '0px 0px -8% 0px', threshold: 0.15 });

  for (i = 0; i < targets.length; i++) io.observe(targets[i]);
})();
