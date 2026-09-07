(() => {
    'use strict';
    let opener, openerId, target, panel, name, observer, resizeObserver, frame;
    const visible = element => element && !element.disabled && !element.closest('[aria-hidden="true"]')
        && element.getClientRects().length > 0 && getComputedStyle(element).visibility !== 'hidden';
    function resolve() {
        const modal = panel?.closest('.mud-dialog');
        return [...(modal || document).querySelectorAll('[data-guide]')].find(e => e.dataset.guide === name && visible(e));
    }
    function position() {
        frame = null;
        if (!panel?.isConnected || panel.classList.contains('so-guide-inline')) return;
        const current = resolve();
        if (current !== target) {
            target?.classList.remove('so-guide-target');
            target = current;
            target?.classList.add('so-guide-target');
        }
        const viewport = window.visualViewport;
        const left = viewport?.offsetLeft || 0, top = viewport?.offsetTop || 0;
        const width = viewport?.width || innerWidth, height = viewport?.height || innerHeight;
        panel.style.width = Math.min(360, width - 24) + 'px';
        panel.style.maxHeight = Math.max(120, height - 24) + 'px';
        const bounds = target?.getBoundingClientRect();
        const box = panel.getBoundingClientRect();
        let x = left + width - box.width - 12, y = top + height - box.height - 12;
        if (bounds) {
            x = Math.min(left + width - box.width - 12, Math.max(left + 12, bounds.left));
            if (bounds.bottom + box.height + 24 < top + height) y = bounds.bottom + 12;
            else if (bounds.top - box.height - 12 >= top + 12) y = bounds.top - box.height - 12;
            else if (bounds.right + box.width + 24 < left + width) { x = bounds.right + 12; y = Math.max(top + 12, bounds.top); }
        }
        panel.style.left = Math.max(left + 12, x) + 'px';
        panel.style.top = Math.max(top + 12, Math.min(y, top + height - box.height - 12)) + 'px';
        panel.dataset.targetAvailable = String(!!target);
    }
    function schedule() { if (!frame) frame = requestAnimationFrame(position); }
    function clear(restoreFocus) {
        if (panel && typeof panel.hidePopover === 'function' && panel.matches(':popover-open')) panel.hidePopover();
        target?.classList.remove('so-guide-target');
        observer?.disconnect();
        resizeObserver?.disconnect();
        removeEventListener('resize', schedule);
        removeEventListener('scroll', schedule, true);
        window.visualViewport?.removeEventListener('resize', schedule);
        window.visualViewport?.removeEventListener('scroll', schedule);
        if (frame) cancelAnimationFrame(frame);
        frame = null;
        target = panel = null;
        const destination = visible(opener) ? opener : document.getElementById(openerId);
        if (restoreFocus && visible(destination)) destination.focus({ preventScroll: true });
    }
    window.secureOpsResourceGuide = {
        begin() { clear(false); opener = document.activeElement; openerId = opener?.id; },
        clear,
        show(targetName, guide) {
            clear(false);
            name = targetName;
            panel = guide;
            // Browser top layer avoids transformed/scroll-clipped Mud dialog ancestors.
            if (typeof panel.showPopover === 'function') panel.showPopover();
            else { panel.removeAttribute('popover'); panel.classList.add('so-guide-inline'); }
            target = resolve();
            if (target) {
                target.classList.add('so-guide-target');
                target.scrollIntoView({ block: 'center', behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth' });
            }
            position();
            addEventListener('resize', schedule);
            addEventListener('scroll', schedule, true);
            window.visualViewport?.addEventListener('resize', schedule);
            window.visualViewport?.addEventListener('scroll', schedule);
            observer = new MutationObserver(schedule);
            observer.observe(document.body, { childList: true, subtree: true });
            resizeObserver = new ResizeObserver(schedule);
            resizeObserver.observe(panel);
            return !!target;
        }
    };
})();
