using UnityEngine;

public class A : MonoBehaviour
{
    void Start()
    {
        // - After 0 seconds, prints "Starting 0.0"
        // - After 0 seconds, prints "Before WaitAndPrint Finishes 0.0"
        // - After 2 seconds, prints "WaitAndPrint 2.0"
        print ("Starting " + Time.time);
        
        // Start function WaitAndPrint as a coroutine.
        var b = new B();
        b.StartCoroutine("WaitAndPrint");

        print ("Before WaitAndPrint Finishes " + Time.time);
    }
}

public class B : MonoBehaviour
{
    private IEnumerator WaitAndPrint() {
        while (true) {
            yield return new WaitForSeconds(2.0f);
            print("WaitAndPrint " + Time.time);
        }
    }
}
