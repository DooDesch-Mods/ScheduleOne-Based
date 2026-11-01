namespace Based.Core;

public static class StaffLimit
{
    // A room for one employee gets three, every other room twice its limit.
    public static int Expanded(int vanilla) => vanilla == 1 ? 3 : vanilla * 2;
}
