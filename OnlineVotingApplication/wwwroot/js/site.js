/* ======================================================
    site.js
    Single shared script for the whole app.

    Section 1 – Admin sidebar (Views/Shared/_AdminSidebar.cshtml)
    Section 2 – Public navbar (Views/Shared/_Navbar.cshtml)
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
        if (!sidebar) return;

        const overlay = document.getElementById('sidebarOverlay');
        const mobileToggleBtn = document.getElementById('sidebarMobileToggle');
        const collapseToggleBtn = document.getElementById('sidebarCollapseToggle');

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

        function closeAllSubmenus() {
            const openCollapsibles = sidebar.querySelectorAll('.collapse.show');
            openCollapsibles.forEach(function (element) {
                if (window.bootstrap && window.bootstrap.Collapse) {
                    const instance = window.bootstrap.Collapse.getOrCreateInstance(element, { toggle: false });
                    instance.hide();
                } else {
                    element.classList.remove('show');
                }
            });
        }

        function toggleDesktopCollapse() {
            const willCollapse = !sidebar.classList.contains('collapsed');
            sidebar.classList.toggle('collapsed');

            if (willCollapse) closeAllSubmenus();

            try {
                localStorage.setItem('adminSidebarCollapsed', willCollapse ? '1' : '0');
            } catch (e) { /* ignore */ }
        }

        if (!isMobileView()) {
            try {
                if (localStorage.getItem('adminSidebarCollapsed') === '1') {
                    sidebar.classList.add('collapsed');
                }
            } catch (e) { /* ignore */ }
        }

        if (mobileToggleBtn) {
            mobileToggleBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                if (isMobileView()) toggleMobileSidebar();
                else toggleDesktopCollapse();
            });
        }

        if (collapseToggleBtn) {
            collapseToggleBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                toggleDesktopCollapse();
            });
        }

        /* ---------------------------------------------------
            Desktop Hover Expansion Feature
        --------------------------------------------------- */
        let hoverTimeout;

        sidebar.addEventListener('mouseenter', function () {
            if (isMobileView() || !sidebar.classList.contains('collapsed')) return;
            clearTimeout(hoverTimeout);
            sidebar.classList.add('sidebar-hover-expanded');
        });

        sidebar.addEventListener('mouseleave', function () {
            if (isMobileView() || !sidebar.classList.contains('collapsed')) return;
            // Slight delay prevents jitter when moving cursor near borders
            hoverTimeout = setTimeout(function () {
                sidebar.classList.remove('sidebar-hover-expanded');
                closeAllSubmenus();
            }, 150);
        });

        if (overlay) {
            overlay.addEventListener('click', closeMobileSidebar);
        }

        sidebar.addEventListener('click', function (e) {
            const link = e.target.closest('a.nav-link');
            if (!link) return;
            const isSubmenuToggle = link.hasAttribute('data-bs-toggle') && link.getAttribute('data-bs-toggle') === 'collapse';
            if (!isSubmenuToggle && isMobileView()) {
                closeMobileSidebar();
            }
        });

        let sidebarResizeTimer;
        window.addEventListener('resize', function () {
            clearTimeout(sidebarResizeTimer);
            sidebarResizeTimer = setTimeout(function () {
                if (!isMobileView()) closeMobileSidebar();
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
        if (!navCollapse) return;

        const NAVBAR_MOBILE_QUERY = window.matchMedia('(max-width: 991.98px)');

        function closeNavCollapse() {
            if (!navCollapse.classList.contains('show')) return;

            if (window.bootstrap && window.bootstrap.Collapse) {
                const instance = window.bootstrap.Collapse.getOrCreateInstance(navCollapse, { toggle: false });
                instance.hide();
            } else {
                navCollapse.classList.remove('show');
                const toggleBtn = document.querySelector('[data-bs-target="#mainNavMenu"]');
                if (toggleBtn) {
                    toggleBtn.setAttribute('aria-expanded', 'false');
                    toggleBtn.classList.add('collapsed');
                }
            }
        }

        navCollapse.addEventListener('click', function (e) {
            const link = e.target.closest('a.nav-link, a.dropdown-item');
            if (!link) return;
            const opensDropdown = link.classList.contains('dropdown-toggle') || link.hasAttribute('data-bs-toggle');
            if (!opensDropdown && NAVBAR_MOBILE_QUERY.matches) {
                closeNavCollapse();
            }
        });

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