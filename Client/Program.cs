using Client;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.CursorVisible = false;

try
{
    var gameApp = new ConsoleGameApp();
    await gameApp.Run();
}
catch (Exception ex)
{
    Console.WriteLine($"Фатальная ошибка: {ex.Message}");
    Console.WriteLine("Нажмите любую клавишу для выхода...");
    Console.ReadKey();
}