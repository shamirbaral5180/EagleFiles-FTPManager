const scrollStorageKey = `eaglefiles-scroll:${window.location.pathname}${window.location.search}`;
if ('scrollRestoration' in history) {
    history.scrollRestoration = 'manual';
}
const savedScrollPosition = Number.parseInt(sessionStorage.getItem(scrollStorageKey) || '', 10);
if (Number.isFinite(savedScrollPosition)) {
    requestAnimationFrame(() => requestAnimationFrame(() => window.scrollTo({ top: savedScrollPosition, behavior: 'instant' })));
}
let scrollSaveFrame = 0;
function saveScrollPosition() {
    sessionStorage.setItem(scrollStorageKey, String(Math.round(window.scrollY)));
}
window.addEventListener('scroll', () => {
    if (scrollSaveFrame) {
        return;
    }
    scrollSaveFrame = requestAnimationFrame(() => {
        saveScrollPosition();
        scrollSaveFrame = 0;
    });
}, { passive: true });
window.addEventListener('pagehide', saveScrollPosition);

const sidebarButton = document.querySelector('[data-sidebar-button]');
const sidebarBackdrop = document.querySelector('.sidebar-backdrop');
const savedSidebarState = localStorage.getItem('eaglefiles-sidebar-collapsed') === 'true';
if (savedSidebarState && window.innerWidth > 900) {
    document.body.classList.add('sidebar-collapsed');
}

function updateSidebarButton() {
    if (!sidebarButton) {
        return;
    }
    const expanded = window.innerWidth <= 900
        ? document.body.classList.contains('sidebar-open')
        : !document.body.classList.contains('sidebar-collapsed');
    sidebarButton.setAttribute('aria-expanded', String(expanded));
    sidebarButton.setAttribute('aria-label', expanded ? 'Hide navigation' : 'Show navigation');
    sidebarButton.title = expanded ? 'Hide navigation' : 'Show navigation';
}

sidebarButton?.addEventListener('click', () => {
    if (window.innerWidth <= 900) {
        document.body.classList.toggle('sidebar-open');
    } else {
        document.body.classList.toggle('sidebar-collapsed');
        localStorage.setItem('eaglefiles-sidebar-collapsed', String(document.body.classList.contains('sidebar-collapsed')));
    }
    updateSidebarButton();
});
sidebarBackdrop?.addEventListener('click', () => {
    document.body.classList.remove('sidebar-open');
    updateSidebarButton();
});
window.addEventListener('resize', () => {
    document.body.classList.remove('sidebar-open');
    updateSidebarButton();
});
updateSidebarButton();

document.querySelectorAll('.toast-alert button').forEach(button => {
    button.addEventListener('click', () => button.closest('.toast-alert')?.remove());
});

document.querySelectorAll('[data-confirm]').forEach(form => {
    form.addEventListener('submit', event => {
        if (!window.confirm(form.dataset.confirm)) {
            event.preventDefault();
        }
    });
});

document.querySelectorAll('[data-password-toggle]').forEach(button => {
    button.addEventListener('click', () => {
        const input = button.parentElement.querySelector('input');
        input.type = input.type === 'password' ? 'text' : 'password';
        button.textContent = input.type === 'password' ? 'Show' : 'Hide';
    });
});

const uploadInput = document.querySelector('[data-upload-input]');
document.querySelector('[data-upload-trigger]')?.addEventListener('click', () => uploadInput?.click());
uploadInput?.addEventListener('change', () => {
    if (uploadInput.files.length) {
        uploadInput.closest('form').submit();
    }
});

const fileContainer = document.querySelector('[data-file-container]');
const sizePicker = document.querySelector('[data-grid-size]');

function setFileDisplay(display) {
    if (!fileContainer) {
        return;
    }

    fileContainer.dataset.display = display;
    document.querySelectorAll('[data-view]').forEach(item => item.classList.toggle('active', item.dataset.view === display));
    sizePicker?.closest('.size-picker')?.classList.toggle('disabled', display === 'list');
    localStorage.setItem('eaglefiles-view', display);
}

document.querySelectorAll('[data-view]').forEach(button => {
    button.addEventListener('click', () => setFileDisplay(button.dataset.view));
});

if (fileContainer) {
    setFileDisplay(localStorage.getItem('eaglefiles-view') || 'grid');
}

if (sizePicker && fileContainer) {
    const savedSize = localStorage.getItem('eaglefiles-grid-size') || 'medium';
    sizePicker.value = savedSize;
    fileContainer.dataset.size = savedSize;
    sizePicker.addEventListener('change', () => {
        fileContainer.dataset.size = sizePicker.value;
        localStorage.setItem('eaglefiles-grid-size', sizePicker.value);
    });
}

const fileSort = document.querySelector('[data-file-sort]');
const fileGroup = document.querySelector('[data-file-group]');
const sortDirection = document.querySelector('[data-sort-direction]');

function getItemDate(item, field) {
    const value = item.dataset[field];
    const timestamp = value ? Date.parse(value) : Number.NaN;
    return Number.isFinite(timestamp) ? timestamp : null;
}

function compareFileItems(left, right) {
    const field = fileSort?.value || 'name';
    const direction = sortDirection?.dataset.direction === 'descending' ? -1 : 1;
    if (field === 'created' || field === 'modified') {
        const leftDate = getItemDate(left, field);
        const rightDate = getItemDate(right, field);
        if (leftDate === null && rightDate !== null) {
            return 1;
        }
        if (leftDate !== null && rightDate === null) {
            return -1;
        }
        if (leftDate !== rightDate) {
            return ((leftDate || 0) - (rightDate || 0)) * direction;
        }
    }
    return left.dataset.fileName.localeCompare(right.dataset.fileName, undefined,
        { numeric: true, sensitivity: 'base' }) * direction;
}

function getGroupDetails(item) {
    const grouping = fileGroup?.value || 'none';
    if (grouping === 'created') {
        const timestamp = getItemDate(item, 'created');
        if (timestamp === null) {
            return { key: 'unknown', label: 'Unknown date', order: Number.NEGATIVE_INFINITY };
        }
        const date = new Date(timestamp);
        const key = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
        return { key, label: date.toLocaleDateString(undefined, { year: 'numeric', month: 'long', day: 'numeric' }), order: timestamp };
    }
    if (grouping === 'type') {
        const labels = {
            folder: 'Folders', image: 'Images', video: 'Videos', audio: 'Audio', pdf: 'PDF documents',
            text: 'Text files', document: 'Documents', archive: 'Archives', other: 'Other files'
        };
        const label = labels[item.dataset.category] || 'Other files';
        return { key: item.dataset.category, label, order: item.dataset.category === 'folder' ? '' : label };
    }
    if (grouping === 'name') {
        const firstCharacter = item.dataset.fileName.trim().charAt(0).toUpperCase();
        const label = /[A-Z0-9]/.test(firstCharacter) ? firstCharacter : '#';
        return { key: label, label, order: label };
    }
    return { key: 'all', label: '', order: '' };
}

function renderFileCollection() {
    if (!fileContainer) {
        return;
    }
    fileContainer.querySelectorAll('[data-group-heading]').forEach(heading => heading.remove());
    const allItems = [...fileContainer.querySelectorAll('[data-file-item]')];
    const groups = new Map();
    allItems.forEach(item => {
        const details = getGroupDetails(item);
        if (!groups.has(details.key)) {
            groups.set(details.key, { ...details, items: [] });
        }
        groups.get(details.key).items.push(item);
    });

    const grouping = fileGroup?.value || 'none';
    const orderedGroups = [...groups.values()].sort((left, right) => {
        if (grouping === 'created') {
            if (left.key === 'unknown') {
                return 1;
            }
            if (right.key === 'unknown') {
                return -1;
            }
            return right.order - left.order;
        }
        return String(left.order).localeCompare(String(right.order), undefined, { numeric: true, sensitivity: 'base' });
    });
    const fragment = document.createDocumentFragment();
    orderedGroups.forEach(group => {
        group.items.sort(compareFileItems);
        if (grouping !== 'none') {
            const heading = document.createElement('div');
            heading.className = 'file-group-heading';
            heading.dataset.groupHeading = '';
            heading.hidden = !group.items.some(item => !item.hidden);
            heading.textContent = group.label;
            fragment.append(heading);
        }
        group.items.forEach(item => fragment.append(item));
    });
    fileContainer.insertBefore(fragment, fileContainer.querySelector('.folder-empty'));
    document.dispatchEvent(new CustomEvent('eaglefiles-order-changed'));
}

if (fileSort && fileGroup && sortDirection) {
    fileSort.value = localStorage.getItem('eaglefiles-file-sort') || 'name';
    fileGroup.value = localStorage.getItem('eaglefiles-file-group') || 'none';
    sortDirection.dataset.direction = localStorage.getItem('eaglefiles-sort-direction') || 'ascending';
    function updateSortDirectionButton() {
        const descending = sortDirection.dataset.direction === 'descending';
        sortDirection.textContent = descending ? '↓' : '↑';
        sortDirection.title = descending ? 'Descending' : 'Ascending';
        sortDirection.setAttribute('aria-label', descending ? 'Sort descending' : 'Sort ascending');
    }
    updateSortDirectionButton();
    fileSort.addEventListener('change', () => {
        sortDirection.dataset.direction = fileSort.value === 'name' ? 'ascending' : 'descending';
        localStorage.setItem('eaglefiles-file-sort', fileSort.value);
        localStorage.setItem('eaglefiles-sort-direction', sortDirection.dataset.direction);
        updateSortDirectionButton();
        renderFileCollection();
    });
    fileGroup.addEventListener('change', () => {
        localStorage.setItem('eaglefiles-file-group', fileGroup.value);
        renderFileCollection();
    });
    sortDirection.addEventListener('click', () => {
        sortDirection.dataset.direction = sortDirection.dataset.direction === 'ascending' ? 'descending' : 'ascending';
        localStorage.setItem('eaglefiles-sort-direction', sortDirection.dataset.direction);
        updateSortDirectionButton();
        renderFileCollection();
    });
    renderFileCollection();
}

document.querySelectorAll('[data-file-filter]').forEach(button => {
    button.addEventListener('click', () => {
        const filter = button.dataset.fileFilter;
        const documentCategories = ['document', 'pdf', 'text', 'archive', 'other'];
        let visibleCount = 0;

        document.querySelectorAll('[data-file-filter]').forEach(item => item.classList.toggle('active', item === button));
        document.querySelectorAll('[data-file-item]').forEach(item => {
            const matches = filter === 'all' || item.dataset.category === filter ||
                (filter === 'document' && documentCategories.includes(item.dataset.category));
            item.hidden = !matches;
            if (matches) {
                visibleCount++;
            }
        });

        const count = document.querySelector('[data-visible-count]');
        if (count) {
            count.textContent = `${visibleCount} item${visibleCount === 1 ? '' : 's'}`;
        }
        const empty = document.querySelector('[data-filter-empty]');
        if (empty) {
            empty.hidden = visibleCount !== 0;
        }
        renderFileCollection();
    });
});

document.querySelectorAll('[data-thumbnail]').forEach(thumbnail => {
    thumbnail.addEventListener('load', () => thumbnail.closest('.file-preview-tile')?.classList.remove('thumbnail-failed'));
    thumbnail.addEventListener('error', () => thumbnail.closest('.file-preview-tile')?.classList.add('thumbnail-failed'));
});

const lightbox = document.querySelector('[data-media-lightbox]');
if (lightbox) {
    let items = [...document.querySelectorAll('[data-viewer-item]:not([hidden])')];
    const mediaHost = lightbox.querySelector('[data-lightbox-media]');
    const stage = lightbox.querySelector('[data-lightbox-stage]');
    const loading = lightbox.querySelector('[data-lightbox-loading]');
    const preloadedImages = new Set();
    let currentIndex = 0;
    let openingIndex = 0;
    let scale = 1;
    let imageOffsetX = 0;
    let imageOffsetY = 0;
    let imageDragging = false;
    let dragPointerX = 0;
    let dragPointerY = 0;
    let highResolutionRequest = 0;

    document.addEventListener('eaglefiles-order-changed', () => {
        items = [...document.querySelectorAll('[data-viewer-item]:not([hidden])')];
    });

    function currentImage() {
        return mediaHost.querySelector('img');
    }

    function fitCurrentVideo() {
        const video = mediaHost.querySelector('video');
        if (!video || !video.videoWidth || !video.videoHeight) {
            return;
        }
        const horizontalMargin = window.innerWidth <= 620 ? 20 : 100;
        const verticalMargin = window.innerWidth <= 620 ? 16 : 24;
        const availableWidth = Math.max(1, stage.clientWidth - horizontalMargin);
        const availableHeight = Math.max(1, stage.clientHeight - verticalMargin);
        const fitScale = Math.min(availableWidth / video.videoWidth, availableHeight / video.videoHeight);
        video.style.width = `${Math.round(video.videoWidth * fitScale)}px`;
        video.style.height = `${Math.round(video.videoHeight * fitScale)}px`;
    }

    function applyTransform() {
        const image = currentImage();
        if (image) {
            image.style.transform = `translate(calc(-50% + ${imageOffsetX}px), calc(-50% + ${imageOffsetY}px)) scale(${scale})`;
        }
    }

    function imageOverflow() {
        const image = currentImage();
        if (!image) {
            return { x: 0, y: 0 };
        }
        return {
            x: Math.max(0, (image.offsetWidth * scale - stage.clientWidth) / 2),
            y: Math.max(0, (image.offsetHeight * scale - stage.clientHeight) / 2)
        };
    }

    function constrainImagePosition() {
        const overflow = imageOverflow();
        imageOffsetX = Math.max(-overflow.x, Math.min(overflow.x, imageOffsetX));
        imageOffsetY = Math.max(-overflow.y, Math.min(overflow.y, imageOffsetY));
        if (overflow.x === 0 && overflow.y === 0) {
            imageOffsetX = 0;
            imageOffsetY = 0;
        }
    }

    function resetTransform() {
        scale = 1;
        imageOffsetX = 0;
        imageOffsetY = 0;
        applyTransform();
    }

    function setScale(nextScale) {
        scale = Math.min(8, Math.max(0.5, nextScale));
        constrainImagePosition();
        applyTransform();
    }

    function preloadAdjacentImages() {
        [-1, 1].forEach(offset => {
            const adjacentItem = items[(currentIndex + offset + items.length) % items.length];
            const source = adjacentItem?.dataset.viewerHigh;
            if (!source || preloadedImages.has(source)) {
                return;
            }
            preloadedImages.add(source);
            const preloadImage = new Image();
            preloadImage.decoding = 'async';
            preloadImage.src = source;
        });
    }

    function showItem(index) {
        if (!items.length) {
            return;
        }

        currentIndex = (index + items.length) % items.length;
        const item = items[currentIndex];
        const type = item.dataset.viewerType;
        const requestId = ++highResolutionRequest;
        const viewerName = lightbox.querySelector('[data-lightbox-name]');
        viewerName.textContent = item.dataset.viewerName;
        viewerName.title = item.dataset.viewerName;
        lightbox.querySelector('[data-lightbox-download]').href = item.dataset.viewerDownload;
        lightbox.classList.toggle('image-active', type === 'image');
        mediaHost.replaceChildren();
        loading.hidden = true;

        if (type === 'image') {
            const gridImage = item.querySelector('[data-thumbnail]');
            const image = document.createElement('img');
            image.alt = item.dataset.viewerName;
            image.draggable = false;
            image.src = gridImage?.currentSrc || gridImage?.src || item.dataset.viewerHigh;
            mediaHost.append(image);
            resetTransform();

            if (item.dataset.viewerHigh && image.src !== new URL(item.dataset.viewerHigh, window.location.href).href) {
                loading.hidden = false;
                const highResolution = new Image();
                highResolution.decoding = 'async';
                highResolution.onload = async () => {
                    if (requestId !== highResolutionRequest) {
                        return;
                    }
                    try {
                        await highResolution.decode();
                    } catch {
                        // The loaded image can still be displayed when explicit decoding is unavailable.
                    }
                    image.src = highResolution.src;
                    loading.hidden = true;
                    preloadAdjacentImages();
                };
                highResolution.onerror = () => {
                    if (requestId === highResolutionRequest) {
                        loading.hidden = true;
                    }
                };
                highResolution.src = item.dataset.viewerHigh;
            } else {
                preloadAdjacentImages();
            }
        } else if (type === 'video') {
            const video = document.createElement('video');
            video.src = item.dataset.viewerPreview;
            video.controls = true;
            video.playsInline = true;
            video.preload = 'metadata';
            const poster = item.querySelector('[data-thumbnail]');
            if (poster && poster.naturalWidth > 0) {
                video.poster = poster.currentSrc || poster.src;
            }
            video.addEventListener('loadedmetadata', fitCurrentVideo, { once: true });
            mediaHost.append(video);
            resetTransform();
        } else if (type === 'audio') {
            const audio = document.createElement('audio');
            audio.src = item.dataset.viewerPreview;
            audio.controls = true;
            audio.autoplay = true;
            mediaHost.append(audio);
            resetTransform();
        } else if (type === 'pdf' || type === 'text') {
            const frame = document.createElement('iframe');
            frame.src = item.dataset.viewerPreview;
            frame.title = item.dataset.viewerName;
            mediaHost.append(frame);
            resetTransform();
        } else {
            const download = document.createElement('a');
            download.className = 'lightbox-file-download';
            download.href = item.dataset.viewerDownload;
            download.setAttribute('aria-label', 'Download file');
            download.textContent = '↓';
            mediaHost.append(download);
            resetTransform();
        }

        lightbox.querySelectorAll('[data-lightbox-reset], [data-lightbox-zoom-out], [data-lightbox-zoom-in]')
            .forEach(control => control.disabled = type !== 'image');
    }

    function openItem(item) {
        openingIndex = items.indexOf(item);
        showItem(openingIndex);
        if (!lightbox.open) {
            lightbox.showModal();
            document.body.classList.add('lightbox-open');
        }
    }

    function closeLightbox() {
        highResolutionRequest++;
        mediaHost.replaceChildren();
        if (document.fullscreenElement) {
            document.exitFullscreen().catch(() => {});
        }
        lightbox.close();
        document.body.classList.remove('lightbox-open');
        if (currentIndex !== openingIndex) {
            requestAnimationFrame(() => {
                items[currentIndex]?.scrollIntoView({ behavior: 'instant', block: 'center', inline: 'nearest' });
                saveScrollPosition();
            });
        }
    }

    document.querySelectorAll('[data-viewer-open="true"]').forEach(link => {
        link.addEventListener('click', event => {
            if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button !== 0) {
                return;
            }
            const viewerItem = link.closest('[data-viewer-item]');
            if (!viewerItem) {
                return;
            }
            event.preventDefault();
            openItem(viewerItem);
        });
    });

    lightbox.querySelector('[data-lightbox-close]').addEventListener('click', closeLightbox);
    lightbox.querySelector('[data-lightbox-previous]').addEventListener('click', () => showItem(currentIndex - 1));
    lightbox.querySelector('[data-lightbox-next]').addEventListener('click', () => showItem(currentIndex + 1));
    lightbox.querySelector('[data-lightbox-reset]').addEventListener('click', resetTransform);
    lightbox.querySelector('[data-lightbox-zoom-in]').addEventListener('click', () => setScale(scale * 1.25));
    lightbox.querySelector('[data-lightbox-zoom-out]').addEventListener('click', () => setScale(scale / 1.25));
    lightbox.querySelector('[data-lightbox-fullscreen]').addEventListener('click', () => {
        resetTransform();
        if (document.fullscreenElement) {
            document.exitFullscreen();
        } else {
            lightbox.requestFullscreen();
        }
    });
    document.addEventListener('fullscreenchange', () => requestAnimationFrame(fitCurrentVideo));
    window.addEventListener('resize', () => requestAnimationFrame(fitCurrentVideo));

    stage.addEventListener('wheel', event => {
        if (!currentImage()) {
            return;
        }
        event.preventDefault();
        setScale(scale * (event.deltaY < 0 ? 1.12 : 0.89));
    }, { passive: false });
    stage.addEventListener('pointerdown', event => {
        const overflow = imageOverflow();
        if (!currentImage() || !event.ctrlKey || event.button !== 0 || (overflow.x === 0 && overflow.y === 0) ||
            event.target.closest('button')) {
            return;
        }
        event.preventDefault();
        imageDragging = true;
        dragPointerX = event.clientX;
        dragPointerY = event.clientY;
        stage.setPointerCapture(event.pointerId);
        stage.classList.add('ctrl-dragging');
    });
    stage.addEventListener('pointermove', event => {
        if (!imageDragging) {
            return;
        }
        imageOffsetX += event.clientX - dragPointerX;
        imageOffsetY += event.clientY - dragPointerY;
        dragPointerX = event.clientX;
        dragPointerY = event.clientY;
        constrainImagePosition();
        applyTransform();
    });
    function stopImageDragging() {
        imageDragging = false;
        stage.classList.remove('ctrl-dragging');
    }
    stage.addEventListener('pointerup', stopImageDragging);
    stage.addEventListener('pointercancel', stopImageDragging);
    stage.addEventListener('dblclick', resetTransform);
    lightbox.addEventListener('click', event => {
        if (event.target === lightbox) {
            closeLightbox();
        }
    });
    lightbox.addEventListener('cancel', event => {
        event.preventDefault();
        closeLightbox();
    });
    document.addEventListener('keydown', event => {
        if (!lightbox.open) {
            return;
        }
        if (event.key === 'ArrowLeft') {
            showItem(currentIndex - 1);
        } else if (event.key === 'ArrowRight') {
            showItem(currentIndex + 1);
        } else if (event.key === '+' || event.key === '=') {
            setScale(scale * 1.25);
        } else if (event.key === '-') {
            setScale(scale / 1.25);
        } else if (event.key.toLowerCase() === 'f') {
            lightbox.querySelector('[data-lightbox-fullscreen]').click();
        }
    });
}

document.querySelectorAll('[data-rename]').forEach(button => {
    button.addEventListener('click', () => {
        const modal = document.querySelector('#renameModal');
        modal.querySelector('[name="currentPath"]').value = button.dataset.path;
        modal.querySelector('[name="newName"]').value = button.dataset.name;
        bootstrap.Modal.getOrCreateInstance(modal).show();
    });
});

document.querySelectorAll('[data-move]').forEach(button => {
    button.addEventListener('click', () => {
        const modal = document.querySelector('#moveModal');
        modal.querySelector('[name="sourcePath"]').value = button.dataset.path;
        modal.querySelector('[name="isDirectory"]').value = button.dataset.directory;
        bootstrap.Modal.getOrCreateInstance(modal).show();
    });
});

document.querySelectorAll('[data-delete]').forEach(button => {
    button.addEventListener('click', () => {
        const modal = document.querySelector('#deleteModal');
        modal.querySelector('[name="targetPath"]').value = button.dataset.path;
        modal.querySelector('[name="isDirectory"]').value = button.dataset.directory;
        modal.querySelector('[data-delete-name]').textContent = button.dataset.name;
        bootstrap.Modal.getOrCreateInstance(modal).show();
    });
});

document.addEventListener('keydown', event => {
    if (event.key === '/' && !['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement.tagName)) {
        event.preventDefault();
        const search = document.querySelector('.browser-search input');
        if (search) {
            if (window.innerWidth <= 620) {
                search.style.display = 'block';
                search.parentElement.style.width = '190px';
            }
            search.focus();
        }
    }
});
