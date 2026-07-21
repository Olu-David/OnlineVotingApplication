/**
 * votex.js — VoteX Layout Scripts
 *
 * 1. Sidebar toggle (collapse ↔ expand) for SuperAdmin desktop
 * 2. Mobile sidebar drawer (slide in/out + overlay)
 * 3. Active nav-link highlighting (backup for server-side active class)
 * 4. Notification badge clear on click
 * 5. Auto-dismiss Bootstrap alerts after 5 s
 */
(function () {
    'use strict';

    /* ── HELPERS ── */
    function $(sel, ctx) { return (ctx || document).querySelector(sel); }
    function $$(sel, ctx) { return Array.from((ctx || document).querySelectorAll(sel)); }

    /* ══════════════════════════════════════════════════════
       1 & 2.  SIDEBAR — desktop collapse + mobile drawer
       ══════════════════════════════════════════════════════ */
    var sidebar = document.getElementById('adminSidebar');
    var toggle = document.getElementById('sidebarToggle');

    if (sidebar && toggle) {

        /* Create overlay for mobile */
        var overlay = document.createElement('div');
        overlay.className = 'vx-sidebar-overlay';
        document.body.appendChild(overlay);

        var isMobile = function () { return window.innerWidth < 992; };

        toggle.addEventListener('click', function () {
            if (isMobile()) {
                /* Mobile: slide-in drawer */
                var open = sidebar.classList.toggle('mobile-open');
                overlay.classList.toggle('show', open);
                toggle.setAttribute('aria-expanded', String(open));
            } else {
                /* Desktop: collapse to icon-only */
                sidebar.classList.toggle('collapsed');
                var isCollapsed = sidebar.classList.contains('collapsed');
                toggle.setAttribute('aria-expanded', String(!isCollapsed));
                /* Persist preference */
                try { localStorage.setItem('vx_sidebar_collapsed', isCollapsed ? '1' : '0'); }
                catch (e) { }
            }
        });

        /* Close drawer when overlay is clicked */
        overlay.addEventListener('click', function () {
            sidebar.classList.remove('mobile-open');
            overlay.classList.remove('show');
            toggle.setAttribute('aria-expanded', 'false');
        });

        /* Restore desktop preference on page load */
        try {
            if (!isMobile() && localStorage.getItem('vx_sidebar_collapsed') === '1') {
                sidebar.classList.add('collapsed');
            }
        } catch (e) { }

        /* Re-evaluate on resize */
        window.addEventListener('resize', function () {
            if (!isMobile()) {
                sidebar.classList.remove('mobile-open');
                overlay.classList.remove('show');
            }
        });
    }


    /* ══════════════════════════════════════════════════════
       3.  ACTIVE LINK HIGHLIGHTING (client-side backup)
           The server-side Razor already sets .active on
           the correct link. This script adds it to any
           exact-path match in case a page is reached via
           redirect without the active class.
       ══════════════════════════════════════════════════════ */
    var currentPath = window.location.pathname.toLowerCase();

    $$('.vx-sidebar-link, .vx-navbar .nav-link').forEach(function (link) {
        if (!link.href) return;
        try {
            var linkPath = new URL(link.href).pathname.toLowerCase();
            /* Match exact path or child paths of the same section */
            if (linkPath !== '/' && currentPath.startsWith(linkPath)) {
                link.classList.add('active');
            }
        } catch (e) { }
    });


    /* ══════════════════════════════════════════════════════
       4.  NOTIFICATION BADGE
       ══════════════════════════════════════════════════════ */
    var notifBtn = document.getElementById('notifBtn');
    var notifBadge = document.getElementById('notifBadge');

    if (notifBtn && notifBadge) {
        notifBtn.addEventListener('click', function () {
            /* Clear badge when panel is opened (replace with real API call) */
            notifBadge.textContent = '0';
            notifBadge.classList.add('d-none');
        });
    }


    /* ══════════════════════════════════════════════════════
       5.  AUTO-DISMISS ALERTS (5 seconds)
           Bootstrap's dismiss button already works via
           data-bs-dismiss. This adds automatic timing.
       ══════════════════════════════════════════════════════ */
    $$('.alert.alert-dismissible').forEach(function (alert) {
        setTimeout(function () {
            /* Use Bootstrap's Alert API if available */
            if (window.bootstrap && window.bootstrap.Alert) {
                var bsAlert = window.bootstrap.Alert.getOrCreateInstance(alert);
                bsAlert.close();
            } else {
                alert.remove();
            }
        }, 5000);
    });

})();