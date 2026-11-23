namespace NC001_ContainsDuplicate;
public class Solution { public bool ContainsDuplicate(int[] nums) => nums.Length != nums.Distinct().Count(); }
