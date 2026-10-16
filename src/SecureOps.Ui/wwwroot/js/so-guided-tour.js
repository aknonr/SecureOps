// Guided tour placement (SoGuidedTour). No feature logic: it finds the element marked data-tour="name", scrolls it into view,
// draws the frame over it, places the box next to it (or leaves the box docked at the bottom below 700 px, see the scoped CSS),
// moves focus to the box heading and, when the tour closes, back to the button that started it. Every decision is made in C#.
const narrowQuery = '(max-width: 700px)';
let current = null;

const visible = element => !!element && element.isConnected && element.getClientRects().length > 0
    && getComputedStyle(element).visibility !== 'hidden'
    && !element.closest('[hidden], [inert], [aria-hidden="true"]');

const find = name => name
    ? [...document.querySelectorAll('[data-tour]')].find(element => element.dataset.tour === name && visible(element)) ?? null
    : null;

const reducedMotion = () => matchMedia('(prefers-reduced-motion: reduce)').matches;

/** For each tour step name, whether its area is on the screen now. */
export function present(names) {
    return (names ?? []).map(name => !!find(name));
}

function place() {
    if (!current) return;
    current.frame = 0;
    const { target, box, ring } = current;
    if (!box?.isConnected) { stop(); return; }
    const viewport = window.visualViewport;
    const width = viewport?.width ?? innerWidth;
    const height = viewport?.height ?? innerHeight;
    const bounds = target?.isConnected ? target.getBoundingClientRect() : null;
    if (ring?.isConnected) {
        if (bounds) {
            ring.style.top = `${bounds.top - 4}px`;
            ring.style.left = `${bounds.left - 4}px`;
            ring.style.width = `${bounds.width + 8}px`;
            ring.style.height = `${bounds.height + 8}px`;
        }
    }
    if (matchMedia(narrowQuery).matches) {
        box.style.left = box.style.top = box.style.right = '';
        return;
    }
    const panel = box.getBoundingClientRect();
    let left = width - panel.width - 12;
    let top = 12;
    if (bounds) {
        left = Math.min(width - panel.width - 12, Math.max(12, bounds.left));
        if (bounds.bottom + panel.height + 24 <= height) top = bounds.bottom + 14;
        else if (bounds.top - panel.height - 14 >= 12) top = bounds.top - panel.height - 14;
        else if (bounds.right + panel.width + 26 <= width) { left = bounds.right + 14; top = Math.max(12, Math.min(bounds.top, height - panel.height - 12)); }
        else top = height - panel.height - 12;
    }
    box.style.right = 'auto';
    box.style.left = `${Math.max(12, left)}px`;
    box.style.top = `${Math.max(12, Math.min(top, height - panel.height - 12))}px`;
}

function schedule() {
    if (current && !current.frame) current.frame = requestAnimationFrame(place);
}

function stop() {
    if (!current) return;
    removeEventListener('resize', schedule);
    removeEventListener('scroll', schedule, true);
    window.visualViewport?.removeEventListener('resize', schedule);
    window.visualViewport?.removeEventListener('scroll', schedule);
    current.resize?.disconnect();
    if (current.frame) cancelAnimationFrame(current.frame);
    if (current.target) current.target.style.scrollMarginTop = current.margin ?? '';
    if (current.padded) document.body.style.paddingBottom = current.padding;
    current = null;
}

/** Highlights the named area (or none), places the box and focuses its heading. Returns whether the area was found. */
export function show(name, box, ring) {
    stop();
    const target = find(name);
    current = { target, box, ring, frame: 0, margin: target?.style.scrollMarginTop };
    if (target) {
        const narrow = matchMedia(narrowQuery).matches;
        if (narrow && box) {
            // Docked box covers the bottom of the screen: keep the area near the top, below the app bar, and give the page room
            // to scroll so an area at the very end can still rise above the box. Restored when the tour moves on or closes.
            target.style.scrollMarginTop = '80px';
            current.padded = true;
            current.padding = document.body.style.paddingBottom;
            document.body.style.paddingBottom = `${Math.ceil(box.getBoundingClientRect().height) + 16}px`;
        }
        target.scrollIntoView({ block: narrow ? 'start' : 'center', inline: 'nearest', behavior: reducedMotion() ? 'instant' : 'smooth' });
    }
    place();
    addEventListener('resize', schedule);
    addEventListener('scroll', schedule, true);
    window.visualViewport?.addEventListener('resize', schedule);
    window.visualViewport?.addEventListener('scroll', schedule);
    if (typeof ResizeObserver === 'function') {
        current.resize = new ResizeObserver(schedule);
        current.resize.observe(box);
        if (target) current.resize.observe(target);
    }
    box?.querySelector('[data-tour-focus]')?.focus({ preventScroll: true });
    return !!target;
}

/** Stops following the page and, when given, returns focus to the button that started the tour. */
export function hide(openerId) {
    stop();
    const opener = openerId ? document.getElementById(openerId) : null;
    if (visible(opener)) opener.focus({ preventScroll: false });
}
