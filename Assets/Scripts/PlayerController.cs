using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float moveSpeed;

    // Update is called once per frame
    void Update()
    {
        Move();
    }

    // player movement
    private void Move()
    {
        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");

        Vector3 moveVector = new Vector3(moveX, 0.0f, moveZ) * moveSpeed * Time.deltaTime;
        transform.Translate(moveVector);
    }
}
