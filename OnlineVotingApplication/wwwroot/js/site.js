/* ======================================================
   site.js
   Single shared script for the whole app.

   Section 1 – Admin sidebar (Views/Shared/_AdminSidebar.cshtml):
     - .admin-sidebar.mobile-open   -> drawer visible (medium/small/very small)
     - .sidebar-overlay.show        -> dark backdrop behind drawer
     - .admin-sidebar.collapsed     -> icon-only sidebar (large screens)

   Section 2 – Public navbar (Views/Shared/_Navbar.cshtml):
     - uses Bootstrap's own collapse for the hamburger, this just adds
       auto-close-on-link-tap for small / very small screens.
   ====================================================== */
(function () {
    'use strict';

    document.addEventListener('DOMContentLoaded', function () {
        initAdminSidebar();
        initPublicNavbar();
    });

    /* ---------------------------------------------------
       Section 1: Admin sidebar
    --------------------------------------------------- */
    function initAdminSidebar() {
        const sidebar = document.getElementById('adminSidebar');
        if (!sidebar) {
            return; // not an admin page, nothing to wire up
        }

        const overlay = document.getElementById('sidebarOverlay');
        const mobileToggleBtn = document.getElementById('sidebarMobileToggle');
        const collapseToggleBtn = document.getElementById('sidebarCollapseToggle');

        // Matches the CSS breakpoint where the sidebar switches to a fixed drawer
        const MOBILE_QUERY = window.matchMedia('(max-width: 991.98px)');

        function isMobileView() {
            return MOBILE_QUERY.matches;
        }

        function openMobileSidebar() {
            sidebar.classList.add('mobile-open');
            if (overlay) overlay.classList.add('show');
            document.body.style.overflow = 'hidden';
            if (mobileToggleBtn) mobileToggleBtn.setAttribute('aria-expanded', 'true');
        }

        function closeMobileSidebar() {
            sidebar.classList.remove('mobile-open');
            if (overlay) overlay.classList.remove('show');
            document.body.style.overflow = '';
            if (mobileToggleBtn) mobileToggleBtn.setAttribute('aria-expanded', 'false');
        }

        function toggleMobileSidebar() {
            if (sidebar.classList.contains('mobile-open')) {
                closeMobileSidebar();
            } else {
                openMobileSidebar();
            }
        }

        function toggleDesktopCollapse() {
            sidebar.classList.toggle('collapsed');
            try {
                localStorage.setItem('adminSidebarCollapsed', sidebar.classList.contains('collapsed') ? '1' : '0');
            } catch (e) {
                /* localStorage unavailable (e.g. private mode) - ignore */
            }
        }

        // Restore the user's collapsed preference on large screens only
        if (!isMobileView()) {
            try {
                if (localStorage.getItem('adminSidebarCollapsed') === '1') {
                    sidebar.classList.add('collapsed');
                }
            } catch (e) {
                /* ignore */
            }
        }

        if (mobileToggleBtn) {
            mobileToggleBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                if (isMobileView()) {
                    toggleMobileSidebar();
                } else {
                    toggleDesktopCollapse();
                }
            });
        }

        if (collapseToggleBtn) {
            collapseToggleBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                toggleDesktopCollapse();
            });
        }

        if (overlay) {
            overlay.addEventListener('click', closeMobileSidebar);
        }

        // Auto-close the drawer after picking a real nav link on small screens
        // (ignores links that only toggle a Bootstrap collapse submenu)
        sidebar.addEventListener('click', function (e) {
            const link = e.target.closest('a.nav-link');
            if (!link) return;
            const opensSubmenu = link.getAttribute('data-bs-toggle') === 'collapse';
            if (!opensSubmenu && isMobileView()) {
                closeMobileSidebar();
            }
        });

        // Keep state sane when crossing the breakpoint (e.g. rotating a phone/tablet)
        let sidebarResizeTimer;
        window.addEventListener('resize', function () {
            clearTimeout(sidebarResizeTimer);
            sidebarResizeTimer = setTimeout(function () {
                if (!isMobileView()) {
                    closeMobileSidebar();
                }
            }, 150);
        });

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && sidebar.classList.contains('mobile-open')) {
                closeMobileSidebar();
            }
        });
    }

    /* ---------------------------------------------------
       Section 2: Public navbar
    --------------------------------------------------- */
    function initPublicNavbar() {
        const navCollapse = document.getElementById('mainNavMenu');
        if (!navCollapse) {
            return; // this page doesn't use the public navbar
        }

        const NAVBAR_MOBILE_QUERY = window.matchMedia('(max-width: 991.98px)');

        function closeNavCollapse() {
            if (!navCollapse.classList.contains('show')) return;

            // Prefer Bootstrap's own Collapse API if it's loaded, so the
            // slide animation and aria attributes stay correct.
            if (window.bootstrap && window.bootstrap.Collapse) {
                const instance = window.bootstrap.Collapse.getOrCreateInstance(navCollapse, { toggle: false });
                instance.hide();
            } else {
                navCollapse.classList.remove('show');
            }
        }

        // Close the mobile menu once a real nav/dropdown-item link is tapped
        // (ignores the dropdown-toggle links, which only open a submenu)
        navCollapse.addEventListener('click', function (e) {
            const link = e.target.closest('a.nav-link, a.dropdown-item');
            if (!link) return;
            const opensDropdown = link.classList.contains('dropdown-toggle');
            if (!opensDropdown && NAVBAR_MOBILE_QUERY.matches) {
                closeNavCollapse();
            }
        });

        // If the viewport is resized back up to desktop width, make sure the
        // collapse isn't left stuck open/closed in a stale state
        let navResizeTimer;
        window.addEventListener('resize', function () {
            clearTimeout(navResizeTimer);
            navResizeTimer = setTimeout(function () {
                if (!NAVBAR_MOBILE_QUERY.matches) {
                    closeNavCollapse();
                }
            }, 150);
        });
    }
})();