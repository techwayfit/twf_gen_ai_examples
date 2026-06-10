window.ingestInterop = {
    _dotNetRef: null,

    init: function (dotNetRef, dropZoneId, fileInputId) {
        this._dotNetRef = dotNetRef;
        var dropZone = document.getElementById(dropZoneId);
        var fileInput = document.getElementById(fileInputId);
        if (!dropZone || !fileInput) return;

        dropZone.addEventListener('dragover', function (e) {
            e.preventDefault();
            e.stopPropagation();
            dropZone.classList.add('dragover');
        });
        dropZone.addEventListener('dragleave', function (e) {
            e.preventDefault();
            e.stopPropagation();
            dropZone.classList.remove('dragover');
        });
        dropZone.addEventListener('drop', function (e) {
            e.preventDefault();
            e.stopPropagation();
            dropZone.classList.remove('dragover');
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnFilesDropped', e.dataTransfer.files.length);
            }
        });
    },

    openFilePicker: function () {
        var el = document.getElementById('fileInput');
        if (el) el.click();
    },

    cleanup: function () {
        this._dotNetRef = null;
    }
};
