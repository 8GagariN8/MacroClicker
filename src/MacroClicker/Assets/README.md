# Иконка MacroClicker

Оригинальный вектор: MacroClicker.svg. Белый указатель без ножки обозначает ввод, три соединённых мятных узла — последовательность действий. Тёмная нейтральная основа согласована с палитрой приложения.

MacroClicker.ico содержит размеры 16, 20, 24, 32, 48, 64, 128 и 256. ICO встроен в Windows EXE и управляемые ресурсы для значков окон; внешняя папка Assets для запуска не нужна.

Пересоздание из вектора на Mac (librsvg и ImageMagick нужны только для изменения иконки):

    rsvg-convert -w 512 -h 512 Assets/MacroClicker.svg -o Assets/MacroClicker.png
    magick Assets/MacroClicker.png -define icon:auto-resize=256,128,64,48,32,24,20,16 Assets/MacroClicker.ico
