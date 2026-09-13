

document.addEventListener('DOMContentLoaded', function () {

    

    var savedTheme = localStorage.getItem('theme') || 'light';

    if (savedTheme === 'dark') {
        document.documentElement.setAttribute('data-theme', 'dark');
        updateThemeUI('dark');
    } else {
        document.documentElement.setAttribute('data-theme', 'light');
        updateThemeUI('light');
    }

    var toggleBtn = document.getElementById('themeToggleBtn') || document.getElementById('themeToggle');
    var themeIcon = document.getElementById('themeIcon');

    function toggleTheme() {
        var currentTheme = document.documentElement.getAttribute('data-theme');
        var newTheme = currentTheme === 'dark' ? 'light' : 'dark';

        document.documentElement.setAttribute('data-theme', newTheme);
        localStorage.setItem('theme', newTheme);
        updateThemeUI(newTheme);

        console.log('🌓 Theme changed to: ' + newTheme);
    }

    function updateThemeUI(theme) {
        if (themeIcon) {
            if (theme === 'dark') {
                themeIcon.className = 'fas fa-sun';
            } else {
                themeIcon.className = 'fas fa-moon';
            }
        }
    }

    if (toggleBtn) {
        toggleBtn.addEventListener('click', function (e) {
            e.stopPropagation();
            toggleTheme();
        });
        console.log('✅ Theme toggle button attached');
    } else {
        console.log('❌ Theme toggle button NOT found');
    }

    

    var cards = document.querySelectorAll('.card');

    cards.forEach(function (card) {
        card.addEventListener('mouseenter', function () {
            this.style.transition = 'transform 0.3s ease, box-shadow 0.3s ease';
        });

        card.addEventListener('mouseleave', function () {
            this.style.transform = '';
            this.style.boxShadow = '';
        });
    });

    

    var allButtons = document.querySelectorAll('.btn-primary, .btn-secondary, .btn-google, .btn-outline, .btn-login, .btn-send');

    allButtons.forEach(function (btn) {
        btn.addEventListener('click', function (e) {
            this.style.transition = 'transform 0.1s';
            this.style.transform = 'scale(0.96)';
            setTimeout(function () {
                this.style.transform = 'scale(1)';
            }.bind(this), 100);
        });
    });

   

    cards.forEach(function (card) {
        card.setAttribute('tabindex', '0');
        card.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                var firstBtn = this.querySelector('.card-back .btn-primary, .card-back .btn-secondary, .card-back .btn-google, .card-back .btn-outline');
                if (firstBtn) {
                    firstBtn.click();
                }
            }
        });
    });


    document.querySelectorAll('a[href^="#"]').forEach(function (anchor) {
        anchor.addEventListener('click', function (e) {
            var targetId = this.getAttribute('href');
            if (targetId && targetId !== '#') {
                var target = document.querySelector(targetId);
                if (target) {
                    e.preventDefault();
                    target.scrollIntoView({ behavior: 'smooth' });
                }
            }
        });
    });

   

    function showToast(message) {
        var toast = document.getElementById('customToast');
        if (!toast) {
            toast = document.createElement('div');
            toast.id = 'customToast';
            toast.style.cssText = 'position: fixed; bottom: 100px; left: 50%; transform: translateX(-50%); background: #ffffff; color: #1e293b; padding: 14px 28px; border-radius: 12px; font-weight: 500; box-shadow: 0 8px 30px rgba(0,0,0,0.15); display: none; z-index: 999; animation: slideUp 0.3s ease; max-width: 90%; text-align: center; border: 1px solid #e2e8f0;';
            document.body.appendChild(toast);
        }

        toast.innerHTML = message;
        toast.style.display = 'block';

        setTimeout(function () {
            toast.style.display = 'none';
        }, 4000);
    }


    var contactForm = document.getElementById('contactForm');
    if (contactForm) {
        contactForm.addEventListener('submit', function (e) {
            e.preventDefault();

            var name = document.getElementById('contactName') ? document.getElementById('contactName').value : '';
            var email = document.getElementById('contactEmail') ? document.getElementById('contactEmail').value : '';
            var subject = document.getElementById('contactSubject') ? document.getElementById('contactSubject').value : '';
            var message = document.getElementById('contactMessage') ? document.getElementById('contactMessage').value : '';

            if (!name || !email || !subject || !message) {
                showToast('⚠️ Please fill in all fields.');
                return;
            }

            showToast('✅ Thank you ' + name + '! Your message has been sent successfully.');
            contactForm.reset();
        });
    }

    

    console.log('🚻 SE1 - Public Washroom Finder');
    console.log('🌓 Theme: ' + document.documentElement.getAttribute('data-theme'));
    console.log('📌 Card interactions active.');
});