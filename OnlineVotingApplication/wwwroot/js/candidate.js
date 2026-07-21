// ===== CANDIDATE LAYOUT JAVASCRIPT =====

document.addEventListener('DOMContentLoaded', function () {
    // Get DOM elements
    const toggleBtn = document.getElementById('toggleSidebar');
    const sidebar = document.getElementById('sidebar');

    // ===== SIDEBAR TOGGLE =====
    // Toggle sidebar on mobile
    if (toggleBtn && sidebar) {
        toggleBtn.addEventListener('click', function () {
            sidebar.classList.toggle('show');
        });

        // Close sidebar when menu item is clicked (mobile)
        document.querySelectorAll('.menu-item').forEach(item => {
            item.addEventListener('click', function () {
                if (window.innerWidth < 768) {
                    sidebar.classList.remove('show');
                }
            });
        });
    }

    // ===== ACTIVE MENU ITEM =====
    // Highlight current page in sidebar
    const currentUrl = window.location.pathname;
    document.querySelectorAll('.menu-item').forEach(item => {
        const href = item.getAttribute('href');
        if (currentUrl.includes(href)) {
            item.style.backgroundColor = 'rgba(255, 107, 53, 0.2)';
            item.style.borderLeftColor = '#FF6B35';
        }
    });

    // ===== RESPONSIVE BEHAVIOR =====
    window.addEventListener('resize', function () {
        if (window.innerWidth >= 768) {
            sidebar.classList.remove('show');
        }
    });

    console.log('✅ Candidate layout loaded');
});