// SPIKE (docs/WEB-VIEWS.md, WV-9a, "Fonts"): the same text block, built and measured the same way in the design
// surface and in the live Kubuno web app (injected there over the DevTools protocol by the comparison script), so the
// two renderings can be compared number for number. Uses only the page's own CSS variables (--font-family-sans,
// --font-family-mono), never a family name of its own.
(function () {
  'use strict';

  const LINES = [
    { key: 'title', text: 'Notes settings', style: 'font-size:21.5px;font-weight:600' },
    { key: 'body', text: 'Edit the elements, drop Toolbox items, undo with Ctrl+Z.', style: 'font-size:13.5px;font-weight:500' },
    { key: 'meta', text: 'Meta text 0123456789 · Ünïcödé àéîõü', style: 'font-size:11.5px;font-weight:500' },
    { key: 'medium', text: 'Medium weight 600 — Kubuno', style: 'font-size:13.5px;font-weight:600' },
    { key: 'italic', text: 'Italic emphasis, a real drawn cut', style: 'font-size:13.5px;font-weight:500;font-style:italic' },
    { key: 'light', text: 'Light 300 and Extra-bold 800', style: 'font-size:15.5px;font-weight:300' },
    { key: 'bold', text: 'Light 300 and Extra-bold 800', style: 'font-size:15.5px;font-weight:800' },
    { key: 'mono', text: 'const x = { id: 42 }; // DM Mono', style: 'font-size:13.5px;font-weight:400;font-family:var(--font-family-mono)' },
  ];

  /** Builds the block in `host` (replacing its content) and returns its measurements once the fonts are loaded. */
  window.kubunoFontProbe = async function (host) {
    host.replaceChildren();
    const block = document.createElement('div');
    block.setAttribute('style', 'font-family:var(--font-family-sans);color:inherit;line-height:normal;padding:12px 16px;' +
      '-webkit-font-smoothing:antialiased;width:max-content');
    for (const line of LINES) {
      const row = document.createElement('div');
      const span = document.createElement('span');
      span.dataset.probe = line.key;
      span.setAttribute('style', line.style);
      span.textContent = line.text;
      row.appendChild(span);
      block.appendChild(row);
    }

    host.appendChild(block);
    await document.fonts.ready;
    const faces = [];
    document.fonts.forEach((f) => faces.push(`${f.family.replace(/"/g, '')} ${f.style} ${f.weight} ${f.status}`));
    const result = {
      dpr: window.devicePixelRatio,
      sans: getComputedStyle(block).fontFamily,
      faces: faces.sort(),
      checks: {
        jakarta500: document.fonts.check('500 13.5px "Plus Jakarta Sans"'),
        jakartaItalic: document.fonts.check('italic 500 13.5px "Plus Jakarta Sans"'),
        dmMono: document.fonts.check('400 13.5px "DM Mono"'),
      },
      lines: {},
      block: { width: block.getBoundingClientRect().width, height: block.getBoundingClientRect().height },
    };
    for (const span of block.querySelectorAll('[data-probe]')) {
      const r = span.getBoundingClientRect();
      result.lines[span.dataset.probe] = { width: Math.round(r.width * 100) / 100, height: Math.round(r.height * 100) / 100 };
    }

    return result;
  };
})();
