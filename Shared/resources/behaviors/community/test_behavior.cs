db.RegisterCommunity("Cutlass Corsair",
    new State(
        new State("idle",
            new Wander(0.4),
            new PlayerWithinTransition(9, "attack")
        ),
        new State("attack",
            new Prioritize(
                new Chase(0.6, range: 3, sightRange: 14),
                new Wander(0.4)
            ),
            new Shoot(10, count: 3, shootAngle: 15, projectileIndex: 0, coolDown: 1000),
            new TimedTransition(5000, "idle")
        )
    )
);
