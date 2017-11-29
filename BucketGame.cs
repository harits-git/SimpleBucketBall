using System;
using System.Collections.Generic;
using Windows.ApplicationModel.Core;
using Urho;
using Urho.Gui;
using Urho.HoloLens;
using Urho.Physics;
using Urho.Resources;
using System.Diagnostics;
using Urho.Audio;

namespace Physics
{
	internal class Program
	{
		[MTAThread]
		static void Main() => CoreApplication.Run(
			new UrhoAppViewSource<BucketGame>(new ApplicationOptions("Data")));
	}

	public class BucketGame : HoloApplication
	{
		Node environmentNode;
        Node bucketNode, collNode;
        Node textNode, soundNode;
        Material spatialMaterial;
		Material bucketMaterial;

        bool surfaceIsValid;
		bool positionIsSelected;
		float bucketScale = 0.1f;

		const int MaxBalls = 3;
		readonly Queue<Node> balls = new Queue<Node>();
		readonly Color validPositionColor = Color.Green;
		readonly Color invalidPositionColor = Color.Red;
        
        float currentBallScale = 0.15f;

        private Text textElement, scoreElement;
        private float power = 0.0f;
        private SoundSource soundFX;
        private SoundSource goalSound;

        public BucketGame(ApplicationOptions assets) : base(assets) { }

		protected override async void Start()
		{
			base.Start();
			environmentNode = Scene.CreateChild();

			// Allow tap gesture
			EnableGestureTapped = true;

			// Create a bucket
			bucketNode = Scene.CreateChild();
			bucketNode.SetScale(0.15f);

			// Create instructions
			textNode = bucketNode.CreateChild();
			
			// Model and Physics for the bucket
			var bucketModel = bucketNode.CreateComponent<StaticModel>();
			bucketMaterial = Material.FromColor(validPositionColor);
			bucketModel.Model = ResourceCache.GetModel("Models/Bucket.mdl");
            bucketModel.SetMaterial(bucketMaterial);            

            bucketModel.ViewMask = 0x80000000; //hide from raycasts
			bucketNode.CreateComponent<RigidBody>();
			var shape = bucketNode.CreateComponent<CollisionShape>();
			shape.SetTriangleMesh(bucketModel.Model, 0, Vector3.One, Vector3.Zero, Quaternion.Identity);

            // ball in the bucket detection
            collNode = bucketNode.CreateChild();
            collNode.Scale *= 2f;
            collNode.Position = new Vector3(0f, 1f, 0);
            var collModel = collNode.CreateComponent<StaticModel>();
            collModel.Model = CoreAssets.Models.Sphere;
            collModel.SetMaterial(Material.FromColor(Color.Yellow));
            collModel.ViewMask = 0x80000000; //hide from raycasts
            SetupNode(collNode);
            
            //--------------------------
            textElement = new Text()
            {
                Value = "Throw gauge:" + power,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                EffectColor = Color.Red,
            };

            textElement.SetFont(CoreAssets.Fonts.AnonymousPro, 18);
            UI.Root.AddChild(textElement);

            scoreElement = new Text()
            {
                Value = "Score: " + score,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Position = new IntVector2(0,35),
                EffectStrokeThickness = 3
            };

            scoreElement.SetFont(CoreAssets.Fonts.AnonymousPro, 24);
            UI.Root.AddChild(scoreElement);
            //--------------------------
            
            //sound---------------------
            soundNode = Scene.CreateChild("sound");
            soundFX = soundNode.CreateComponent<SoundSource>();            
            soundFX.Gain = 0.8f;
            //sound---------------------


            // Material for spatial surfaces
            spatialMaterial = new Material();//Material.FromImage("Textures/stone.jpg");//
			spatialMaterial.SetTechnique(0, CoreAssets.Techniques.NoTextureUnlitVCol, 1, 1);
            //spatialMaterial.SetTechnique(0,CoreAssets.Techniques.Diff,1,1);

			// make sure 'spatialMapping' capabilaty is enabled in the app manifest.
			var spatialMappingAllowed = await StartSpatialMapping(new Vector3(50, 50, 10), 1200);
            await TextToSpeech("Welcome to Bucket Ball game!");
            Debug.WriteLine("Ready");
		}
        bool scoreIsAdded = false;
        protected void SetupNode(Node node) { 
            
            var body = node.CreateComponent<RigidBody>();
            //body.Trigger = false;

            var shape = node.CreateComponent<CollisionShape>();
            shape.SetSphere(1, Vector3.Zero, Quaternion.Identity); //SetBox(new Vector3(1, 1, 1), Vector3.Zero, Quaternion.Identity);
            node.NodeCollisionStart += (args) =>
            {
                //node.RemoveComponent<RigidBody>();
                args.OtherNode.Remove();
                if(!scoreIsAdded)
                    UpdateScore();
                Debug.WriteLine("Collision with {0}", args.OtherNode);
                
            };
        }

        bool isIncreasing = true;
        private void obtainingPower()
        {

            if (isIncreasing)
            {
                power += 0.05f;
                if(power >= 15)
                {
                    isIncreasing = false;
                }
            }
            else
            {
                power -= 0.05f;
                if (power <= 0)
                {
                    isIncreasing = true;
                }
            }
            textElement.Value = "Throw gauge: "+power;
        }

        int score = 0;
        
        protected override void OnUpdate(float timeStep)
		{
			if (positionIsSelected)
            {
                //if(isThrown)
                //    CheckBallInBucket();
                if(isObtainingPower)
                    obtainingPower();
                return;
            }


            //textNode.Translate(RightCamera.Node.Position);
			textNode.LookAt(LeftCamera.Node.WorldPosition, new Vector3(0, 1, 0), TransformSpace.World);
			textNode.Rotate(new Quaternion(0, 180, 0), TransformSpace.World);

			Ray cameraRay = RightCamera.GetScreenRay(0.5f, 0.5f);
			var result = Scene.GetComponent<Octree>().RaycastSingle(cameraRay, RayQueryLevel.Triangle, 100, DrawableFlags.Geometry, 0x70000000);
			if (result != null)
			{
				var angle = Vector3.CalculateAngle(new Vector3(0, 1, 0), result.Value.Normal);
				surfaceIsValid = angle < 0.3f; //allow only horizontal surfaces
                if (surfaceIsValid)
                {
                    textElement.Value = "Valid Location\nTap to start game!!";
                }else
                {
                    textElement.Value = "Move bucket!!";
                }
				bucketMaterial.SetShaderParameter("MatDiffColor", surfaceIsValid ? validPositionColor : invalidPositionColor);
                
				bucketNode.Position = result.Value.Position;
			}
			else
			{
				// no spatial surfaces found
				surfaceIsValid = false;
				bucketMaterial.SetShaderParameter("MatDiffColor", validPositionColor);
			}
		}

        bool powerObtained = false;
        bool isObtainingPower = false;
		public override void OnGestureTapped()
		{
            if (positionIsSelected)
                if (!isObtainingPower)
                    isObtainingPower = true;
                else
                {
                    isObtainingPower = false;
                    powerObtained = true;
                }

                if (powerObtained)
                {
                    ThrowBall();
                    powerObtained = false;
                    power = 0.0f;
                }                            


            if (surfaceIsValid && !positionIsSelected)
			{
				positionIsSelected = true;
                bucketNode.GetComponent<StaticModel>().SetMaterial(Material.FromImage("Textures/woodGrain-vertical.jpg"));
            }

			base.OnGestureTapped();
		}

        public override void OnGestureDoubleTapped()
        {            
            base.OnGestureDoubleTapped();
        }


        void UpdateScore()
        {
            score++;
            scoreIsAdded = true;
            scoreElement.Value = "Score: "+score;
            soundFX.Play(ResourceCache.GetSound("Sounds/okay.wav"));
        }

		void ThrowBall()
		{
            scoreIsAdded = false;
			// Create a ball (will be cloned)
			var ballNode = Scene.CreateChild();
			ballNode.Position = RightCamera.Node.Position;
			ballNode.Rotation = RightCamera.Node.Rotation;
			ballNode.SetScale(currentBallScale);
            soundFX.Play(ResourceCache.GetSound("Sounds/bamboo-swing.wav"));

            var ball = ballNode.CreateComponent<StaticModel>();
			ball.Model = CoreAssets.Models.Sphere;
            ball.SetMaterial(Material.FromImage("Textures/basketball.png"));// (Material.FromColor(Randoms.NextColor()));
			ball.ViewMask = 0x80000000; //hide from raycasts

			var ballRigidBody = ballNode.CreateComponent<RigidBody>();
			ballRigidBody.Mass = 1f;
			ballRigidBody.RollingFriction = 0.5f;
			var ballShape = ballNode.CreateComponent<CollisionShape>();
            ballShape.SetSphere(1, Vector3.Zero, Quaternion.Identity);
            
			ball.GetComponent<RigidBody>().SetLinearVelocity(RightCamera.Node.Rotation * new Vector3(0f, 0.25f, 1f) * power /*velocity*/);
            
            
		}

		public override void OnSurfaceAddedOrUpdated(SpatialMeshInfo surface, Model generatedModel)
		{
			bool isNew = false;
			StaticModel staticModel = null;
			Node node = environmentNode.GetChild(surface.SurfaceId, false);
			if (node != null)
			{
				isNew = false;
				staticModel = node.GetComponent<StaticModel>();
			}
			else
			{
				isNew = true;
				node = environmentNode.CreateChild(surface.SurfaceId);
				staticModel = node.CreateComponent<StaticModel>();
			}

			node.Position = surface.BoundsCenter;
			node.Rotation = surface.BoundsRotation;
			staticModel.Model = generatedModel;

			if (isNew)
			{
				staticModel.SetMaterial(spatialMaterial);
				var rigidBody = node.CreateComponent<RigidBody>();
				rigidBody.RollingFriction = 0.5f;
				rigidBody.Friction = 0.5f;
				var collisionShape = node.CreateComponent<CollisionShape>();
				collisionShape.SetTriangleMesh(generatedModel, 0, Vector3.One, Vector3.Zero, Quaternion.Identity);
			}
			else
			{
				//Update Collision shape
			}
		}
	}
}