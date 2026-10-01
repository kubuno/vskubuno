// SPIKE (docs/WEB-VIEWS.md, lot WV-9a): the WebView2 design surface test page.
// - renders the document's markup (sent by Visual Studio in setText) as a few @kubuno/ui-like elements, each root DOM
//   node tagged data-kb-id with the desktop element id (dot path of element-child ordinals, "" = root);
// - design mode: a glass layer takes every pointer event (input neutralisation), hit-tests with elementsFromPoint and
//   draws hover / parent / selection adorners; F2 or a double-click edits a Text inline;
// - Toolbox drops through either channel (host: dragEnter/dragOver/drop/dragLeave messages from Visual Studio's OLE
//   drop target; html5: Chromium's own drag and drop, the Toolbox item's text/plain);
// - never writes text: every gesture becomes an editRequest that Visual Studio applies to its buffer (one undo unit).
// WV-10 replaces this with @kubuno/views' design mode on the real renderer.
(function () {
  'use strict';

  const webview = window.chrome && window.chrome.webview;
  const post = (message) => { if (webview) { webview.postMessage(message); } };
  const log = (message) => post({ type: 'log', message: String(message) });

  const view = document.getElementById('view');
  const canvas = document.getElementById('canvas');
  const glass = document.getElementById('glass');
  const hover = document.getElementById('hover');
  const parentBox = document.getElementById('parent');
  const selectionBox = document.getElementById('selection');
  const marker = document.getElementById('marker');
  const editor = document.getElementById('inline-editor');
  const errorBox = document.getElementById('error');
  const status = document.getElementById('status');
  const specimen = document.getElementById('specimen');

  /** name -> { container, xml } (setComponents). */
  let components = new Map();
  let selectedId = null;
  let hoverId = null;
  let dropChannel = 'host';
  let dragComponent = null;
  let lastTargetKey = '';
  let editingId = null;
  let editingText = false;

  // ---- rendering ----

  const isContainer = (name) => {
    const c = components.get(name);
    return c ? c.container : false;
  };

  function childElements(node) {
    return Array.from(node.childNodes).filter((n) => n.nodeType === 1);
  }

  function render(markup) {
    const parsed = new DOMParser().parseFromString(markup, 'application/xml');
    const failure = parsed.getElementsByTagName('parsererror')[0];
    if (failure) {
      const message = failure.textContent || 'The view markup is not well-formed.';
      errorBox.textContent = message;
      errorBox.style.display = 'block';
      const position = /line (\d+)[^\d]+(\d+)/i.exec(message);
      post({ type: 'surfaceError', message, line: position ? Number(position[1]) : 0, column: position ? Number(position[2]) : 0 });
      return; // keep the last good rendering, like the desktop surface
    }

    errorBox.style.display = 'none';
    view.replaceChildren(build(parsed.documentElement, ''));
    if (selectedId !== null && !findNode(selectedId)) {
      select(null, true);
    } else {
      updateAdorners();
    }
  }

  function build(element, id) {
    const name = element.localName;
    const attr = (key, fallback) => (element.hasAttribute(key) ? element.getAttribute(key) : fallback);
    let node;
    switch (name) {
      case 'Stack': {
        node = document.createElement('div');
        node.className = 'kb-stack';
        node.style.flexDirection = attr('Direction', 'TopDown') === 'LeftToRight' ? 'row' : 'column';
        node.style.gap = (Number(attr('Gap', '0')) || 0) + 'px';
        node.style.padding = (Number(attr('Padding', '0')) || 0) + 'px';
        node.style.alignItems = node.style.flexDirection === 'row' ? 'center' : 'stretch';
        break;
      }
      case 'Label':
        node = document.createElement('div');
        node.className = 'kb-label ' + attr('TextStyle', 'Body').toLowerCase();
        node.textContent = attr('Text', '');
        break;
      case 'Button':
        node = document.createElement('button');
        node.className = 'kb-button ' + attr('Variant', 'Secondary').toLowerCase();
        node.textContent = attr('Text', '');
        node.disabled = attr('Enabled', 'true') === 'false';
        break;
      case 'TextField':
        node = document.createElement('input');
        node.className = 'kb-textfield';
        node.value = attr('Text', '');
        node.placeholder = attr('Placeholder', '');
        node.readOnly = true;
        break;
      case 'CheckBox': {
        node = document.createElement('label');
        node.className = 'kb-checkbox';
        const box = document.createElement('input');
        box.type = 'checkbox';
        box.checked = attr('Checked', 'false') === 'true';
        box.tabIndex = -1;
        const text = document.createElement('span');
        text.textContent = attr('Text', '');
        node.append(box, text);
        break;
      }
      default:
        node = document.createElement('div');
        node.className = 'kb-placeholder';
        node.textContent = name;
        break;
    }

    node.dataset.kbId = id;
    node.dataset.kbName = name;
    node.tabIndex = -1;
    const children = childElements(element);
    children.forEach((child, index) => node.appendChild(build(child, id === '' ? String(index) : id + '.' + index)));
    if (name === 'Stack' && children.length === 0) {
      node.classList.add('empty');
    }

    if (id === '') {
      node.classList.add('kb-root');
    }

    return node;
  }

  const findNode = (id) => (id === null ? null : view.querySelector(`[data-kb-id="${CSS.escape(id)}"]`));
  const parentIdOf = (id) => (id === '' ? null : id.includes('.') ? id.slice(0, id.lastIndexOf('.')) : '');

  // ---- hit test and adorners ----

  /** The deepest element under a viewport point, in reverse paint order (LayoutMap::hit_test). */
  function hitTest(x, y) {
    for (const element of document.elementsFromPoint(x, y)) {
      const tagged = element.closest('[data-kb-id]');
      if (tagged && view.contains(tagged)) {
        return tagged;
      }
    }

    return null;
  }

  function boundsOf(node) {
    const r = node.getBoundingClientRect();
    return { x: r.left, y: r.top, width: r.width, height: r.height };
  }

  function place(box, node, inflate) {
    if (!node) {
      box.style.display = 'none';
      return;
    }

    const r = node.getBoundingClientRect();
    box.style.display = 'block';
    box.style.left = r.left - inflate + 'px';
    box.style.top = r.top - inflate + 'px';
    box.style.width = r.width + 2 * inflate + 'px';
    box.style.height = r.height + 2 * inflate + 'px';
  }

  function updateAdorners() {
    const selected = findNode(selectedId);
    place(selectionBox, selected, 3);
    place(parentBox, selectedId !== null && selectedId !== '' ? findNode(parentIdOf(selectedId)) : null, 1);
    place(hover, hoverId !== null && hoverId !== selectedId ? findNode(hoverId) : null, 0);
    if (editingId !== null) {
      placeEditor();
    }
  }

  function select(id, notify) {
    selectedId = id;
    updateAdorners();
    if (notify) {
      const node = findNode(id);
      post({ type: 'selectionChanged', id, ids: id === null ? [] : [id], bounds: node ? boundsOf(node) : null });
    }
  }

  // ---- pointer (design mode: the glass gets everything) ----

  glass.addEventListener('pointermove', (e) => {
    const node = hitTest(e.clientX, e.clientY);
    const id = node ? node.dataset.kbId : null;
    if (id !== hoverId) {
      hoverId = id;
      updateAdorners();
    }
  });
  glass.addEventListener('pointerleave', () => { hoverId = null; updateAdorners(); });
  glass.addEventListener('pointerdown', (e) => {
    if (editingId !== null) {
      commitEdit();
    }

    glass.focus();
    const node = hitTest(e.clientX, e.clientY);
    select(node ? node.dataset.kbId : '', true);
  });
  glass.addEventListener('dblclick', (e) => {
    const node = hitTest(e.clientX, e.clientY);
    if (node) {
      beginEdit(node.dataset.kbId);
    }
  });
  glass.addEventListener('contextmenu', (e) => e.preventDefault());
  glass.addEventListener('focus', () => document.body.classList.add('focused'));
  glass.addEventListener('blur', () => document.body.classList.remove('focused'));
  canvas.addEventListener('scroll', updateAdorners);
  window.addEventListener('resize', () => { updateAdorners(); sendMetrics(); });

  // ---- keyboard (only the keys Visual Studio left to the page: AcceleratorRouting) ----

  glass.addEventListener('keydown', (e) => {
    const id = selectedId;
    let handled = true;
    switch (e.key) {
      case 'ArrowLeft':
      case 'ArrowUp':
      case 'ArrowRight':
      case 'ArrowDown': {
        if (id === null || id === '') {
          select(childElements(view.firstElementChild || view).length ? '0' : '', true);
          break;
        }

        const parentId = parentIdOf(id);
        const siblings = childElements(findNode(parentId));
        const index = Number(id.slice(id.lastIndexOf('.') + 1));
        const step = e.key === 'ArrowLeft' || e.key === 'ArrowUp' ? -1 : 1;
        const next = Math.max(0, Math.min(siblings.length - 1, index + step));
        select(siblings[next].dataset.kbId, true);
        break;
      }
      case 'Enter': {
        const node = findNode(id);
        if (node && childElements(node).length > 0) {
          select(childElements(node)[0].dataset.kbId, true);
        }
        break;
      }
      case 'Escape':
        if (id !== null && id !== '') {
          select(parentIdOf(id), true);
        }
        break;
      case 'F2':
        beginEdit(id);
        break;
      default:
        handled = false;
        break;
    }

    if (handled) {
      e.preventDefault();
    } else if (e.key !== 'Tab' && e.key !== 'Shift' && e.key !== 'Control' && e.key !== 'Alt') {
      // Measurement: a key the page received but does not use (VS should have taken it).
      log(`key reached the page: ${e.ctrlKey ? 'Ctrl+' : ''}${e.altKey ? 'Alt+' : ''}${e.shiftKey ? 'Shift+' : ''}${e.key}`);
    }
  });

  // ---- inline text editing (typing goes to the page) ----

  function beginEdit(id) {
    const node = findNode(id);
    if (!node || !['Label', 'Button', 'CheckBox', 'TextField'].includes(node.dataset.kbName)) {
      return;
    }

    if (selectedId !== id) {
      select(id, true);
    }

    editingId = id;
    const text = node.dataset.kbName === 'CheckBox' ? node.querySelector('span').textContent
      : node.dataset.kbName === 'TextField' ? node.value : node.textContent;
    editor.value = text;
    placeEditor();
    editor.style.display = 'block';
    editor.focus();
    editor.select();
  }

  function placeEditor() {
    const node = findNode(editingId);
    if (!node) {
      return;
    }

    const r = node.getBoundingClientRect();
    editor.style.left = r.left - 2 + 'px';
    editor.style.top = r.top - 2 + 'px';
    editor.style.width = Math.max(r.width + 4, 120) + 'px';
    editor.style.height = r.height + 4 + 'px';
  }

  function endEdit(commit) {
    if (editingId === null) {
      return;
    }

    const id = editingId;
    const value = editor.value;
    editingId = null;
    editor.style.display = 'none';
    if (commit) {
      post({ type: 'editRequest', op: { kind: 'setAttribute', elementId: id, name: 'Text', value } });
    }

    glass.focus();
  }

  const commitEdit = () => endEdit(true);
  editor.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      endEdit(true);
    } else if (e.key === 'Escape') {
      e.preventDefault();
      endEdit(false);
    }
  });
  editor.addEventListener('focus', () => setEditing(true));
  editor.addEventListener('blur', () => { setEditing(false); if (editingId !== null) { endEdit(true); } });

  function setEditing(on) {
    if (editingText !== on) {
      editingText = on;
      post({ type: 'focusState', editing: on });
    }
  }

  // ---- Toolbox drops ----

  /** Where a drop at (x, y) lands: the container under the point and the child index, with the marker line. */
  function dropTarget(x, y) {
    let node = hitTest(x, y);
    while (node && !isContainer(node.dataset.kbName)) {
      node = node.parentElement ? node.parentElement.closest('[data-kb-id]') : null;
    }

    if (!node || !view.contains(node)) {
      return null;
    }

    const row = getComputedStyle(node).flexDirection === 'row';
    const box = node.getBoundingClientRect();
    const style = getComputedStyle(node);
    const padStart = parseFloat(row ? style.paddingLeft : style.paddingTop) || 0;
    const padEnd = parseFloat(row ? style.paddingRight : style.paddingBottom) || 0;
    const kids = childElements(node);
    let index = kids.length;
    for (let i = 0; i < kids.length; i++) {
      const r = kids[i].getBoundingClientRect();
      if ((row ? x : y) < (row ? r.left + r.width / 2 : r.top + r.height / 2)) {
        index = i;
        break;
      }
    }

    let at;
    if (kids.length === 0) {
      at = (row ? box.left : box.top) + padStart + 1;
    } else if (index < kids.length) {
      const r = kids[index].getBoundingClientRect();
      const previous = index > 0 ? kids[index - 1].getBoundingClientRect() : null;
      at = previous ? ((row ? previous.right : previous.bottom) + (row ? r.left : r.top)) / 2 : (row ? r.left : r.top) - 2;
    } else {
      const r = kids[kids.length - 1].getBoundingClientRect();
      at = Math.min((row ? r.right : r.bottom) + 2, (row ? box.right : box.bottom) - padEnd);
    }

    const thickness = 2;
    const line = row
      ? { markerLeft: at - 1, markerTop: box.top + 2, markerRight: at - 1 + thickness, markerBottom: box.bottom - 2 }
      : { markerLeft: box.left + 2, markerTop: at - 1, markerRight: box.right - 2, markerBottom: at - 1 + thickness };
    return Object.assign({ valid: true, parentId: node.dataset.kbId, index }, line);
  }

  function showTarget(target) {
    const key = target ? `${target.parentId}|${target.index}|${Math.round(target.markerLeft)}|${Math.round(target.markerTop)}` : '';
    if (target) {
      marker.style.display = 'block';
      marker.className = target.valid ? 'marker' : 'marker invalid';
      marker.style.left = target.markerLeft + 'px';
      marker.style.top = target.markerTop + 'px';
      marker.style.width = target.markerRight - target.markerLeft + 'px';
      marker.style.height = target.markerBottom - target.markerTop + 'px';
      hoverId = target.parentId;
    } else {
      marker.style.display = 'none';
      hoverId = null;
    }

    updateAdorners();
    if (key !== lastTargetKey) {
      lastTargetKey = key;
      post({ type: 'dropTargetChanged', target });
    }
  }

  function dropAt(x, y, component) {
    const target = dropTarget(x, y);
    showTarget(null);
    const definition = components.get(component);
    const xml = definition ? definition.xml : `<${component}/>`;
    if (target && target.valid) {
      post({ type: 'editRequest', op: { kind: 'insertChild', parentId: target.parentId, index: target.index, xml } });
    }

    return target;
  }

  // Channel "html5": Chromium's drag and drop with the Toolbox item's text/plain (kubuno-toolbox:<Name>). The data is
  // only readable at drop time (protected mode), so the marker is drawn without knowing the item.
  // Channel "host": the same detection, then Visual Studio takes the drag over (an OLE drop target raised over the
  // page); from then on the page only follows the host's dragEnter/dragOver/drop/dragLeave.
  const hasText = (e) => e.dataTransfer && Array.from(e.dataTransfer.types).includes('text/plain');
  const isToolboxDrag = (e) => dropChannel === 'html5' && hasText(e);
  let detected = false;
  const detect = (e) => {
    if (dropChannel === 'host' && hasText(e)) {
      e.preventDefault();
      if (!detected) {
        detected = true;
        post({ type: 'toolboxDragDetected' });
      }
      return true;
    }
    return false;
  };
  document.addEventListener('dragenter', (e) => { if (!detect(e) && isToolboxDrag(e)) { e.preventDefault(); } });
  document.addEventListener('dragover', (e) => {
    if (detect(e) || !isToolboxDrag(e)) {
      return;
    }

    e.preventDefault();
    e.dataTransfer.dropEffect = 'copy';
    showTarget(dropTarget(e.clientX, e.clientY));
  });
  document.addEventListener('dragleave', (e) => {
    if (dropChannel === 'html5' && (e.clientX <= 0 || e.clientY <= 0 || e.clientX >= innerWidth || e.clientY >= innerHeight)) {
      showTarget(null);
    }
  });
  document.addEventListener('drop', (e) => {
    if (!isToolboxDrag(e)) {
      return;
    }

    e.preventDefault();
    const text = e.dataTransfer.getData('text/plain');
    const match = /^kubuno-toolbox:([A-Za-z][A-Za-z0-9_]*)$/.exec(text.trim());
    log(`html5 drop: text/plain="${text}" at ${e.clientX},${e.clientY}`);
    if (match) {
      dropAt(e.clientX, e.clientY, match[1]);
    } else {
      showTarget(null);
    }
  });

  // ---- Visual Studio -> page ----

  function sendMetrics() {
    post({ type: 'metrics', dpr: window.devicePixelRatio, width: window.innerWidth, height: window.innerHeight });
    status.textContent = `dpr ${window.devicePixelRatio} · ${window.innerWidth}×${window.innerHeight} CSS px · drop: ${dropChannel}`;
  }

  // A per-monitor DPI change (or a RasterizationScale change) changes devicePixelRatio: re-measure.
  (function watchDpr() {
    const query = matchMedia(`(resolution: ${window.devicePixelRatio}dppx)`);
    query.addEventListener('change', () => { sendMetrics(); updateAdorners(); watchDpr(); }, { once: true });
  })();

  const handlers = {
    setText: (m) => render(m.text),
    select: (m) => select(m.id, false),
    setDesignMode: (m) => { glass.style.display = m.on ? 'block' : 'none'; },
    setComponents: (m) => { components = new Map(m.components.map((c) => [c.name, c])); },
    setCanvasBackground: (m) => document.documentElement.style.setProperty('--vs-canvas', m.color),
    setVsTheme: (m) => {
      for (const [name, value] of Object.entries(m.colors || {})) {
        document.documentElement.style.setProperty('--vs-' + name, value);
      }

      document.documentElement.dataset.kbTheme = m.mode;
      document.documentElement.style.colorScheme = m.mode;
    },
    setDropChannel: (m) => { dropChannel = m.channel; showTarget(null); sendMetrics(); },
    dragEnter: (m) => { dragComponent = m.component; detected = false; },
    dragOver: (m) => { if (dragComponent) { showTarget(dropTarget(m.x, m.y)); } },
    drop: (m) => { if (dragComponent) { dropAt(m.x, m.y, dragComponent); } dragComponent = null; detected = false; },
    dragLeave: () => { dragComponent = null; detected = false; showTarget(null); },
    showFontSpecimen: (m) => { specimen.className = m.on ? 'shown' : 'offscreen'; },
  };

  if (webview) {
    webview.addEventListener('message', (e) => {
      const handler = e.data && handlers[e.data.type];
      if (handler) {
        handler(e.data);
      } else {
        log('unknown host message: ' + JSON.stringify(e.data).slice(0, 200));
      }
    });
  }

  post({ type: 'surfaceInfo', version: 1, target: 'web', views: '0.0.0-spike', ui: 'spike' });
  sendMetrics();

  // Font check (WV-9a): the production faces must be the ones drawn; the measurements are compared with the live app's.
  if (window.kubunoFontProbe) {
    window.kubunoFontProbe(specimen).then((r) => log('font-probe ' + JSON.stringify(r)));
  }
})();
