// MudBlazor 6 MudSelect accessibility bridge (TH-13). One place for every MudSelect in the application.
//
// What MudSelect 6 renders inside .mud-input-control.mud-select:
//   - a hidden <input> that carries the attributes the page passes (aria-label), but is not focusable;
//   - the keyboard target, one of two shapes: <div class="mud-input-slot" tabindex="0"> showing the selected item, or, when no
//     registered item matches the current value, a visible read-only <input type="text"> with that div hidden (tabindex -1);
//   - a <label for> that points at an id nobody owns, so nothing is named by it;
//   - the options as role-less <div class="mud-list-item"> in a popover appended elsewhere in the page.
// MudBlazor handles Enter, Space, arrows and Escape on the target. Nothing here replaces that; it only adds the semantics a screen
// reader or a role-based test needs:
//   target -> role=combobox, accessible name (aria-label from the hidden input, else aria-labelledby the visible label),
//             aria-haspopup, aria-expanded, aria-controls (the list), aria-activedescendant (the highlighted option; focus stays on the
//             target and the arrow keys move mud-selected-item), aria-disabled when the control is disabled
//   list   -> role=listbox, named like the combobox; items -> role=option, aria-selected, aria-disabled
// Idempotent and attribute-only. Keep it free of feature logic; Blazor owns state.
(function () {
    'use strict';

    var sequence = 0;
    var managed = ['role', 'aria-haspopup', 'aria-expanded', 'aria-controls', 'aria-activedescendant', 'aria-disabled'];

    function ensureId(element, prefix) {
        if (!element.id) { element.id = prefix + (++sequence); }
        return element.id;
    }

    function setAttribute(element, name, value) {
        if (element.getAttribute(name) !== value) { element.setAttribute(name, value); }
    }

    function removeAttribute(element, name) {
        if (element.hasAttribute(name)) { element.removeAttribute(name); }
    }

    function enhance(control) {
        var target = control.querySelector('div.mud-input-slot[tabindex="0"]')
            || control.querySelector('input.mud-select-input:not([type="hidden"])');

        // The other shape of the same control may have been the target on an earlier render; do not leave a second combobox behind.
        control.querySelectorAll('div.mud-input-slot, input.mud-select-input').forEach(function (candidate) {
            if (candidate === target) { return; }
            managed.forEach(function (name) { removeAttribute(candidate, name); });
            removeAttribute(candidate, 'aria-labelledby');
        });
        if (!target) { return; }

        var hidden = control.querySelector('input[aria-label]');
        var label = control.querySelector('label.mud-input-label');
        var nameAttribute = null;
        var nameValue = null;
        if (hidden) { nameAttribute = 'aria-label'; nameValue = hidden.getAttribute('aria-label'); }
        else if (label) { nameAttribute = 'aria-labelledby'; nameValue = ensureId(label, 'so-select-label-'); }

        setAttribute(target, 'role', 'combobox');
        setAttribute(target, 'aria-haspopup', 'listbox');
        if (nameAttribute) {
            removeAttribute(target, nameAttribute === 'aria-label' ? 'aria-labelledby' : 'aria-label');
            setAttribute(target, nameAttribute, nameValue);
        }
        if (control.classList.contains('mud-disabled')) { setAttribute(target, 'aria-disabled', 'true'); }
        else { removeAttribute(target, 'aria-disabled'); }

        // The popover placeholder inside the control and its content share a GUID: popover-<guid> / popovercontent-<guid>.
        var placeholder = control.querySelector('.mud-popover-cascading-value[id^="popover-"]');
        var content = placeholder ? document.getElementById('popovercontent-' + placeholder.id.substring('popover-'.length)) : null;
        var open = !!content && content.classList.contains('mud-popover-open');
        setAttribute(target, 'aria-expanded', open ? 'true' : 'false');

        var list = content ? content.querySelector('.mud-list') : null;
        if (!list) {
            removeAttribute(target, 'aria-controls');
            removeAttribute(target, 'aria-activedescendant');
            return;
        }
        setAttribute(list, 'role', 'listbox');
        ensureId(list, 'so-select-list-');
        if (nameAttribute) { setAttribute(list, nameAttribute, nameValue); }
        setAttribute(target, 'aria-controls', list.id);

        var active = null;
        list.querySelectorAll('.mud-list-item').forEach(function (item) {
            var selected = item.classList.contains('mud-selected-item');
            setAttribute(item, 'role', 'option');
            setAttribute(item, 'aria-selected', selected ? 'true' : 'false');
            if (item.classList.contains('mud-list-item-disabled')) { setAttribute(item, 'aria-disabled', 'true'); }
            else { removeAttribute(item, 'aria-disabled'); }
            if (selected) { active = item; }
        });
        if (open && active && active.id) { setAttribute(target, 'aria-activedescendant', active.id); }
        else { removeAttribute(target, 'aria-activedescendant'); }
    }

    function run() {
        document.querySelectorAll('.mud-input-control.mud-select').forEach(enhance);
    }

    var scheduled = false;
    function schedule() {
        if (scheduled) { return; }
        scheduled = true;
        window.requestAnimationFrame(function () { scheduled = false; run(); });
    }

    // Blazor re-renders replace nodes and toggle popover classes. The attributes written above are not observed (only `class`
    // changes and node additions are), so this does not re-trigger itself.
    new MutationObserver(schedule).observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
    run();
}());
